-- FollowMe Render | v0.1.0 | 3,545 chars
FollowMe = FollowMe or {}
FollowMe.Render = {}

local config = FollowMe.Config

local function ApplyColor(frame, color)
  frame:SetBackgroundColor(color[1], color[2], color[3], color[4])
end

local function ComputeUiScale(rootFrame, profile)
  local rootWidth = tonumber(rootFrame:GetWidth()) or profile.windowWidth
  local rootHeight = tonumber(rootFrame:GetHeight()) or profile.windowHeight
  local scaleX = tonumber(profile.displayScaleX) or tonumber(profile.displayScale) or 1
  local scaleY = tonumber(profile.displayScaleY) or tonumber(profile.displayScale) or 1

  if scaleX <= 0 then
    scaleX = 1
  end
  if scaleY <= 0 then
    scaleY = 1
  end

  return scaleX, scaleY, rootWidth, rootHeight
end

local function ApplyLayout(renderState)
  local profile = renderState.profile
  local scaleX, scaleY, rootWidth, rootHeight = ComputeUiScale(renderState.rootFrame, profile)
  local index

  renderState.lastRootWidth = rootWidth
  renderState.lastRootHeight = rootHeight

  if renderState.lastScaleX == scaleX and renderState.lastScaleY == scaleY then
    return false
  end

  if renderState.band.ClearAllPoints ~= nil then
    renderState.band:ClearAllPoints()
  end
  renderState.band:SetPoint("TOPLEFT", renderState.rootFrame, "TOPLEFT", 0, 0)
  renderState.band:SetWidth(profile.bandWidth * scaleX)
  renderState.band:SetHeight(profile.bandHeight * scaleY)

  for index = 1, profile.segmentCount do
    local segment = renderState.segments[index]
    if segment.ClearAllPoints ~= nil then
      segment:ClearAllPoints()
    end
    segment:SetPoint("TOPLEFT", renderState.band, "TOPLEFT", (index - 1) * profile.segmentWidth * scaleX, 0)
    segment:SetWidth(profile.segmentWidth * scaleX)
    segment:SetHeight(profile.segmentHeight * scaleY)
  end

  renderState.lastScaleX = scaleX
  renderState.lastScaleY = scaleY
  return true
end

function FollowMe.Render.Initialize(rootFrame)
  local profile = config.profile
  local band = UI.CreateFrame("Frame", "FollowMeBand", rootFrame)
  local segments = {}
  local lastSymbols = {}
  local index

  band:SetPoint("TOPLEFT", rootFrame, "TOPLEFT", 0, 0)
  band:SetWidth(profile.bandWidth)
  band:SetHeight(profile.bandHeight)
  band:SetLayer(config.requestedLayer)
  ApplyColor(band, config.GetPaletteColor(0))

  for index = 1, profile.segmentCount do
    local segment = UI.CreateFrame("Frame", "FollowMeSegment" .. tostring(index), band)
    segment:SetPoint("TOPLEFT", band, "TOPLEFT", (index - 1) * profile.segmentWidth, 0)
    segment:SetWidth(profile.segmentWidth)
    segment:SetHeight(profile.segmentHeight)
    segment:SetLayer(config.requestedLayer + 1)
    ApplyColor(segment, config.GetPaletteColor(0))
    segments[index] = segment
    lastSymbols[index] = -1
  end

  return {
    profile = profile,
    rootFrame = rootFrame,
    band = band,
    segments = segments,
    lastSymbols = lastSymbols,
    lastScaleX = nil,
    lastScaleY = nil,
    lastRootWidth = nil,
    lastRootHeight = nil
  }
end

function FollowMe.Render.SyncLayout(renderState)
  return ApplyLayout(renderState)
end

function FollowMe.Render.Update(renderState, symbols)
  local changed = false
  local index

  if ApplyLayout(renderState) then
    changed = true
  end

  for index = 1, renderState.profile.segmentCount do
    local symbol = symbols[index] or 0
    if renderState.lastSymbols[index] ~= symbol then
      ApplyColor(renderState.segments[index], config.GetPaletteColor(symbol))
      renderState.lastSymbols[index] = symbol
      changed = true
    end
  end

  return changed
end

-- FollowMe Render | v0.1.0 | END
