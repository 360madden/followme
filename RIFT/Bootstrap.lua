FollowMe = FollowMe or {}
FollowMe.Bootstrap = {}

local addonIdentifier = FollowMe.Config.addonIdentifier

local function GetRealtimeNow()
  if Inspect ~= nil and Inspect.Time ~= nil and Inspect.Time.Real ~= nil then
    return Inspect.Time.Real()
  end
  return 0
end

local function GetStatsPageSchemaId(pageIndex)
  local normalized = tonumber(pageIndex) or 1
  if normalized == 1 then
    return 1
  end
  if normalized == 2 then
    return 2
  end
  if normalized == 3 then
    return 3
  end
  if normalized == 4 then
    return 4
  end
  return 5
end

local function GetPlayerStatsSnapshotForFrame(state, now)
  local refreshPeriod = FollowMe.Config.statsSnapshotRefreshSeconds or 0.50
  local shouldRefresh = state.lastStatsSnapshot == nil

  if not shouldRefresh then
    local lastCollectedAt = state.lastStatsCollectedAt or 0
    if now <= 0 or lastCollectedAt <= 0 then
      shouldRefresh = true
    elseif (now - lastCollectedAt) >= refreshPeriod then
      shouldRefresh = true
    end
  end

  if shouldRefresh then
    if FollowMe.Config.syntheticMode ~= nil and FollowMe.Config.syntheticMode.enabled then
      state.lastStatsSnapshot = FollowMe.Gather.BuildSyntheticPlayerStatsSnapshot()
    else
      state.lastStatsSnapshot = FollowMe.Gather.BuildPlayerStatsSnapshot()
    end

    state.lastStatsCollectedAt = now
  end

  return state.lastStatsSnapshot
end

function FollowMe.Bootstrap.Refresh(forceRefresh, reason)
  local state = FollowMe.Bootstrap.state
  local now
  local snapshot
  local statsSnapshot
  local _, symbols
  local statsInterval
  local sendStatsPage

  if state == nil then
    return
  end

  now = GetRealtimeNow()
  if not forceRefresh and now > 0 and (now - state.lastRefreshAt) < FollowMe.Config.refreshIntervalSeconds then
    return
  end

  -- ── MultiBox frame interleaving ───────────────────────────────────────────
  -- When multibox leader mode is active, interleave position and state frames
  -- into the sequence. Priority: position > multibox state > existing frames.
  local multiBoxMode = FollowMe.MultiBox and FollowMe.MultiBox.GetMode() or "off"

  if multiBoxMode == "leader" then
    local posInterval = FollowMe.Config.multiBoxPositionInterval or 3
    local mbInterval = FollowMe.Config.multiBoxStateInterval or 6

    if math.fmod(state.sequence, mbInterval) == 0 then
      -- MultiBoxState frame
      local mbSnapshot = FollowMe.MultiBox.BuildMultiBoxStateSnapshot()
      _, symbols = FollowMe.Protocol.BuildMultiBoxStateFrame(mbSnapshot, state.sequence)
      state.lastFrameKind = "multibox-state"
      FollowMe.Render.Update(state.render, symbols)
      state.lastRefreshAt = now
      state.lastReason = reason
      state.sequence = math.fmod(state.sequence + 1, 256)
      return
    end

    if math.fmod(state.sequence, posInterval) == 0 then
      -- PlayerPosition frame
      local posSnapshot = FollowMe.MultiBox.BuildPositionSnapshot()
      _, symbols = FollowMe.Protocol.BuildPlayerPositionFrame(posSnapshot, state.sequence)
      state.lastFrameKind = "player-position"
      FollowMe.Render.Update(state.render, symbols)
      state.lastRefreshAt = now
      state.lastReason = reason
      state.sequence = math.fmod(state.sequence + 1, 256)
      return
    end
  end

  -- ── Existing frame logic (unchanged) ─────────────────────────────────────
  statsInterval = FollowMe.Config.statsPageFrameInterval or 5
  sendStatsPage = math.fmod(state.sequence + 1, statsInterval) == 0

  if sendStatsPage then
    statsSnapshot = GetPlayerStatsSnapshotForFrame(state, now)

    _, symbols = FollowMe.Protocol.BuildPlayerStatsPageFrame(
      statsSnapshot,
      state.sequence,
      GetStatsPageSchemaId(state.nextStatsPageIndex))

    state.lastFrameKind = "player-stats-page-" .. tostring(state.nextStatsPageIndex)
    state.nextStatsPageIndex = math.fmod(state.nextStatsPageIndex, FollowMe.Config.statsPageCount or 5) + 1
  else
    if FollowMe.Config.syntheticMode ~= nil and FollowMe.Config.syntheticMode.enabled then
      snapshot = FollowMe.Gather.BuildSyntheticCoreStatusSnapshot()
    else
      snapshot = FollowMe.Gather.BuildCoreStatusSnapshot()
    end
    _, symbols = FollowMe.Protocol.BuildCoreFrame(snapshot, state.sequence)

    state.lastSnapshot = snapshot
    state.lastFrameKind = "core-status"
  end

  FollowMe.Render.Update(state.render, symbols)

  state.lastRefreshAt = now
  state.lastReason = reason
  state.sequence = math.fmod(state.sequence + 1, 256)
end

function FollowMe.Bootstrap.SafeRefresh(forceRefresh, reason)
  local ok, failure = pcall(function()
    FollowMe.Bootstrap.Refresh(forceRefresh, reason)
  end)

  if not ok then
    FollowMe.Diagnostics.Log("Refresh failed: " .. tostring(failure))
  end
end

function FollowMe.Bootstrap.Initialize()
  if FollowMe.Bootstrap.state ~= nil then
    return
  end

  local context = UI.CreateContext("FollowMeContext")
  local root = UI.CreateFrame("Frame", "FollowMeRoot", context)
  root:SetAllPoints(context)
  root:SetVisible(true)
  root:SetLayer(FollowMe.Config.requestedLayer)

  if FollowMe.Config.requestedStrata ~= nil then
    context:SetStrata(FollowMe.Config.requestedStrata)
  end

  FollowMe.Bootstrap.state = {
    context = context,
    root = root,
    render = FollowMe.Render.Initialize(root),
    lastRefreshAt = 0,
    lastReason = "startup",
    lastSnapshot = nil,
    lastStatsSnapshot = nil,
    lastStatsCollectedAt = 0,
    lastFrameKind = "none",
    nextStatsPageIndex = 1,
    sequence = 0
  }

  FollowMe.Render.SyncLayout(FollowMe.Bootstrap.state.render)
  FollowMe.Diagnostics.Log("Initialized P360C segmented color strip for full-size 640x24 live rendering.")
  FollowMe.Diagnostics.Log("Core heartbeat plus cached paged player stats transport is enabled.")
  if FollowMe.Config.syntheticMode ~= nil and FollowMe.Config.syntheticMode.enabled then
    FollowMe.Diagnostics.Log("Synthetic strip mode is enabled.")
  end

  if FollowMe.Bootstrap.state.render.lastScaleX ~= nil and FollowMe.Bootstrap.state.render.lastScaleY ~= nil then
    FollowMe.Diagnostics.Log(string.format(
      "Root layout %.1fx%.1f -> render scale %.3f x %.3f.",
      FollowMe.Bootstrap.state.render.lastRootWidth or 0,
      FollowMe.Bootstrap.state.render.lastRootHeight or 0,
      FollowMe.Bootstrap.state.render.lastScaleX or 1,
      FollowMe.Bootstrap.state.render.lastScaleY or 1))
  end

  FollowMe.Bootstrap.SafeRefresh(true, "initialize")
end

function FollowMe.Bootstrap.OnLoadEnd(_, loadedAddonIdentifier)
  if loadedAddonIdentifier ~= addonIdentifier then
    return
  end

  if FollowMe.Config.showOnStartup then
    FollowMe.Diagnostics.Log("Load event received for v" .. FollowMe.Config.addonVersion .. ".")
    FollowMe.Bootstrap.Initialize()
  end
end

function FollowMe.Bootstrap.OnUpdateBegin()
  if FollowMe.Bootstrap.state == nil then
    return
  end

  FollowMe.Bootstrap.SafeRefresh(false, "update")
end

Command.Event.Attach(
  Event.Addon.Load.End,
  FollowMe.Bootstrap.OnLoadEnd,
  "FollowMe.Bootstrap.OnLoadEnd"
)

Command.Event.Attach(
  Event.System.Update.Begin,
  FollowMe.Bootstrap.OnUpdateBegin,
  "FollowMe.Bootstrap.OnUpdateBegin"
)
