using System.ComponentModel;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace FollowMe.Reader;

internal static class DesktopDuplicationCaptureBackend
{
    private const uint AcquireTimeoutMs = 100;

    public static CaptureResult CaptureTopSlice(CaptureTarget target, int captureHeight, CaptureBackend backend)
    {
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        using var output = FindOutput(factory, target.MonitorHandle, out var adapter, out var outputRect);
        using var output1 = output.QueryInterface<IDXGIOutput1>();

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
            FeatureLevel.Level_9_3
        };

        var result = D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out ID3D11Device? device,
            out _,
            out ID3D11DeviceContext? context);

        if (result.Failure || device is null || context is null)
        {
            throw new InvalidOperationException($"Desktop Duplication device creation failed: 0x{result.Code:X8}");
        }

        using var ownedDevice = device;
        using var ownedContext = context;
        using var duplication = output1.DuplicateOutput(ownedDevice);

        if (duplication is null)
        {
            throw new InvalidOperationException("Desktop Duplication could not create an output duplication object.");
        }

        var captureRect = new ScreenRect(
            target.SourceRect.Left,
            target.SourceRect.Top,
            target.SourceRect.Width,
            captureHeight);

        var desktopLeft = captureRect.Left - outputRect.Left;
        var desktopTop = captureRect.Top - outputRect.Top;
        if (desktopLeft < 0 || desktopTop < 0)
        {
            throw new InvalidOperationException("Desktop Duplication crop landed outside the active monitor bounds.");
        }

        IDXGIResource? desktopResource = null;
        var acquired = AcquireDesktopFrame(duplication, out desktopResource);
        if (acquired.Failure || desktopResource is null)
        {
            throw new InvalidOperationException($"Desktop Duplication failed to acquire a frame: 0x{acquired.Code:X8}");
        }

        try
        {
            using var ownedDesktopResource = desktopResource;
            using var desktopTexture = ownedDesktopResource.QueryInterface<ID3D11Texture2D>();
            if (desktopTexture is null)
            {
                throw new InvalidOperationException("Desktop Duplication returned a frame resource that was not a texture.");
            }

            var textureDescription = desktopTexture.Description;
            var textureWidth = (int)textureDescription.Width;
            var textureHeight = (int)textureDescription.Height;
            var width = Math.Min(captureRect.Width, textureWidth - desktopLeft);
            var height = Math.Min(captureRect.Height, textureHeight - desktopTop);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Desktop Duplication crop resolved outside the desktop texture.");
            }

            var stagingDescription = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = textureDescription.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                    MiscFlags = ResourceOptionFlags.None
                };

            var frame = TryCopyViaMappedDesktopSurface(duplication, textureDescription.Format, desktopLeft, desktopTop, width, height);
            if (frame is null)
            {
                frame = CopyViaStagingTexture(ownedDevice, ownedContext, desktopTexture, stagingDescription, desktopLeft, desktopTop, width, height);
            }

            return new CaptureResult(
                frame with { CaptureRouteReason = "desktopdup" },
                backend,
                target.ClientRect.Left,
                target.ClientRect.Top,
                target.ClientRect.Width,
                target.ClientRect.Height,
                captureRect.Left,
                captureRect.Top,
                width,
                height,
                "desktopdup");
        }
        finally
        {
            duplication.ReleaseFrame();
        }
    }

    private static IDXGIOutput FindOutput(IDXGIFactory1 factory, nint monitorHandle, out IDXGIAdapter1 adapter, out ScreenRect outputRect)
    {
        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            var adapterResult = factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? currentAdapter);
            if (adapterResult.Failure || currentAdapter is null)
            {
                break;
            }

            using (currentAdapter)
            {
                for (uint outputIndex = 0; ; outputIndex++)
                {
                    var outputResult = currentAdapter.EnumOutputs(outputIndex, out IDXGIOutput? output);
                    if (outputResult.Failure || output is null)
                    {
                        break;
                    }

                    var description = output.Description;
                    if (description.Monitor == monitorHandle)
                    {
                        adapter = currentAdapter.QueryInterface<IDXGIAdapter1>();
                        outputRect = new ScreenRect(
                            description.DesktopCoordinates.Left,
                            description.DesktopCoordinates.Top,
                            description.DesktopCoordinates.Right - description.DesktopCoordinates.Left,
                            description.DesktopCoordinates.Bottom - description.DesktopCoordinates.Top);
                        return output;
                    }

                    output.Dispose();
                }
            }
        }

        throw new InvalidOperationException("Desktop Duplication could not map the RIFT window to a DXGI output.");
    }

    private static SharpGen.Runtime.Result AcquireDesktopFrame(IDXGIOutputDuplication duplication, out IDXGIResource? desktopResource)
    {
        desktopResource = null;
        SharpGen.Runtime.Result lastResult = default;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            lastResult = duplication.AcquireNextFrame(AcquireTimeoutMs, out var frameInfo, out desktopResource);
            if (lastResult.Failure || desktopResource is null)
            {
                return lastResult;
            }

            if (frameInfo.LastPresentTime != 0 || frameInfo.AccumulatedFrames > 0)
            {
                return lastResult;
            }

            desktopResource.Dispose();
            desktopResource = null;
            duplication.ReleaseFrame();
            Thread.Sleep(15);
        }

        return lastResult;
    }

    private static Bgr24Frame? TryCopyViaMappedDesktopSurface(
        IDXGIOutputDuplication duplication,
        Format format,
        int left,
        int top,
        int width,
        int height)
    {
        try
        {
            var mapped = duplication.MapDesktopSurface();
            try
            {
                var croppedPointer = IntPtr.Add(mapped.DataPointer, (top * (int)mapped.Pitch) + (left * 4));
                return CopyMappedTexture(format, width, height, (int)mapped.Pitch, croppedPointer);
            }
            finally
            {
                _ = duplication.UnMapDesktopSurface();
            }
        }
        catch
        {
            return null;
        }
    }

    private static Bgr24Frame CopyViaStagingTexture(
        ID3D11Device device,
        ID3D11DeviceContext context,
        ID3D11Texture2D desktopTexture,
        Texture2DDescription stagingDescription,
        int left,
        int top,
        int width,
        int height)
    {
        using var stagingTexture = device.CreateTexture2D(stagingDescription);
        var sourceRegion = new Box(left, top, 0, left + width, top + height, 1);
        context.CopySubresourceRegion(stagingTexture, 0, 0, 0, 0, desktopTexture, 0, sourceRegion);

        var mapped = context.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            return CopyMappedTexture(stagingDescription.Format, width, height, (int)mapped.RowPitch, mapped.DataPointer);
        }
        finally
        {
            context.Unmap(stagingTexture, 0);
        }
    }

    private static Bgr24Frame CopyMappedTexture(Format format, int width, int height, int rowPitch, IntPtr dataPointer)
    {
        if (format != Format.B8G8R8A8_UNorm && format != Format.B8G8R8A8_UNorm_SRgb && format != Format.B8G8R8A8_Typeless)
        {
            throw new InvalidOperationException($"Desktop Duplication returned unsupported texture format: {format}.");
        }

        var pixels = new byte[width * height * 3];
        var sourceRow = new byte[width * 4];
        var destinationOffset = 0;

        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(IntPtr.Add(dataPointer, y * rowPitch), sourceRow, 0, sourceRow.Length);
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = x * 4;
                pixels[destinationOffset++] = sourceRow[sourceOffset];
                pixels[destinationOffset++] = sourceRow[sourceOffset + 1];
                pixels[destinationOffset++] = sourceRow[sourceOffset + 2];
            }
        }

        return new Bgr24Frame(width, height, pixels, "desktopdup");
    }
}
