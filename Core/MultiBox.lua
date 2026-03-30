-- FollowMe.MultiBox | v0.1.0 | 210 chars (approx — maintained at finalization)
-- Research findings (2026-03-30):
--   Inspect.Unit.Detail("player").coordX / .coordY / .coordZ — VERIFIED
--   Command.Message.Broadcast("tell", target, msgId, data) — VERIFIED (private, invisible in chat)
--   Command.Message.Accept(type, msgId) — VERIFIED (registers receiver)
--   Event.System.Update.Begin — VERIFIED (fires every frame including combat)
--   Player yaw/facing — NOT EXPOSED by RIFT API (calculated externally from position delta)
--
-- NilRisk: Inspect.Unit.Detail may return nil if player not fully loaded — all calls pcall-wrapped.
-- NilRisk: Command.Message.* may be nil on older RIFT builds — guarded with nil checks.

FollowMe = FollowMe or {}
FollowMe.MultiBox = {}

local config = FollowMe.Config

-- ── Mode ──────────────────────────────────────────────────────────────────────
-- "off"      — no multibox activity (default)
-- "leader"   — broadcasts position and target state each frame tick
-- "follower" — accepts incoming leader messages; fires /assist on target change

local _mode = "off"
local _leaderName = nil    -- set for follower mode: name of leader character
local _lastTargetName = nil

-- ── Public API ────────────────────────────────────────────────────────────────

function FollowMe.MultiBox.SetMode(mode, leaderName)
  _mode = tostring(mode or "off")
  _leaderName = leaderName
  FollowMe.Diagnostics.Log(
    "MultiBox mode: " .. _mode ..
    (leaderName and (" (leader: " .. tostring(leaderName) .. ")") or ""))

  if _mode == "follower" then
    FollowMe.MultiBox.RegisterReceiver()
  end
end

function FollowMe.MultiBox.GetMode()
  return _mode
end

-- ── Position snapshot ─────────────────────────────────────────────────────────

local function SafeUnitDetail(unitRef)
  if Inspect == nil or Inspect.Unit == nil or Inspect.Unit.Detail == nil then
    return nil
  end
  local ok, result = pcall(Inspect.Unit.Detail, unitRef)
  if ok then return result end
  return nil
end

local function SafeUnitLookup(ref)
  if Inspect == nil or Inspect.Unit == nil or Inspect.Unit.Lookup == nil then
    return nil
  end
  local ok, result = pcall(Inspect.Unit.Lookup, ref)
  if ok then return result end
  return nil
end

function FollowMe.MultiBox.BuildPositionSnapshot()
  -- NilRisk: detail may be nil if player not loaded; coordX/Y/Z default to 0
  local detail = SafeUnitDetail("player")
  return {
    x = (detail and detail.coordX) or 0,
    y = (detail and detail.coordY) or 0,
    z = (detail and detail.coordZ) or 0
  }
end

-- ── MultiBox state snapshot ───────────────────────────────────────────────────

function FollowMe.MultiBox.BuildMultiBoxStateSnapshot()
  local player = SafeUnitDetail("player")
  local targetId = SafeUnitLookup("player.target")
  local target = targetId and SafeUnitDetail(targetId) or nil

  local inCombat = player and player.combat or false
  local hasTarget = target ~= nil
  local targetHostile = false
  local targetName = ""

  if target then
    local relation = target.relation or ""
    local lowerRelation = string.lower(tostring(relation))
    targetHostile = string.find(lowerRelation, "hostile", 1, true) ~= nil
                 or string.find(lowerRelation, "enemy", 1, true) ~= nil
    -- NilRisk: target.name may be nil
    targetName = (target.name or ""):sub(1, 10)
  end

  return {
    inCombat = inCombat,
    hasTarget = hasTarget,
    targetHostile = targetHostile,
    targetName = targetName
  }
end

-- ── Leader broadcast ──────────────────────────────────────────────────────────
-- Called by Bootstrap on each frame tick when mode == "leader".
-- Returns position frame bytes+symbols, and multibox state on interval.

function FollowMe.MultiBox.GetLeaderFrames(state)
  -- state: Bootstrap state table (contains .sequence, .render, etc.)
  if _mode ~= "leader" then
    return nil, nil
  end

  local seq = state.sequence
  local posSnapshot = FollowMe.MultiBox.BuildPositionSnapshot()
  local _, posSymbols = FollowMe.Protocol.BuildPlayerPositionFrame(posSnapshot, seq)

  -- Interleave MultiBoxState frame every multiBoxStateInterval sequences
  local mbSymbols = nil
  local mbInterval = config.multiBoxStateInterval or 6
  if math.fmod(seq, mbInterval) == 0 then
    local mbSnapshot = FollowMe.MultiBox.BuildMultiBoxStateSnapshot()
    _, mbSymbols = FollowMe.Protocol.BuildMultiBoxStateFrame(mbSnapshot, seq)
  end

  return posSymbols, mbSymbols
end

-- ── Follower: receive leader messages via Command.Message ─────────────────────
-- The leader Lua addon can optionally broadcast target name via private message
-- as a supplement to the visual protocol channel (for longer target names).

function FollowMe.MultiBox.RegisterReceiver()
  if Command == nil or Command.Message == nil or Command.Message.Accept == nil then
    FollowMe.Diagnostics.Log("MultiBox: Command.Message.Accept not available; skipping receiver registration.")
    return
  end

  local ok, err = pcall(function()
    Command.Message.Accept("tell", config.multiBoxChannelId)
  end)

  if not ok then
    FollowMe.Diagnostics.Log("MultiBox: Failed to register message receiver: " .. tostring(err))
  else
    FollowMe.Diagnostics.Log("MultiBox: Registered message receiver for channel: " .. config.multiBoxChannelId)
  end
end

function FollowMe.MultiBox.OnMessageReceived(messageType, senderName, channelId, data)
  -- Only process messages for our channel from the leader
  if channelId ~= config.multiBoxChannelId then return end
  if _leaderName and senderName ~= _leaderName then return end
  if _mode ~= "follower" then return end

  -- data format: "target:<targetName>" or "assist:<targetName>"
  if data == nil then return end
  local dataStr = tostring(data)

  local targetName = string.match(dataStr, "^target:(.+)$")
  if targetName and targetName ~= _lastTargetName then
    _lastTargetName = targetName
    FollowMe.MultiBox.AssistTarget(targetName)
  end
end

-- ── Follower: target assist ───────────────────────────────────────────────────
-- Issues /assist via Command.Slash if available, with a fallback log message.
-- NilRisk: Command.Slash may not exist on all RIFT builds.

function FollowMe.MultiBox.AssistTarget(leaderName)
  if _mode ~= "follower" then return end
  local name = leaderName or _leaderName
  if name == nil then
    FollowMe.Diagnostics.Log("MultiBox: AssistTarget called with no leader name.")
    return
  end

  -- Command.Slash is not a standard RIFT API — /assist is done via C# keypress injection.
  -- This Lua function is provided as a fallback for environments where it IS available,
  -- and logs the intent for diagnostics otherwise.
  if Command ~= nil and Command.Slash ~= nil then
    local ok, err = pcall(Command.Slash, "/assist " .. name)
    if not ok then
      FollowMe.Diagnostics.Log("MultiBox: Assist failed: " .. tostring(err))
    end
  else
    FollowMe.Diagnostics.Log("MultiBox: Would assist " .. tostring(name) .. " (C# handles injection)")
  end
end

-- ── Follower: broadcast own target to leader (optional) ───────────────────────
-- Not used in v1 but reserved for future bidirectional state.

-- ── Module-level event: receive incoming messages ────────────────────────────
-- Attach to Event.Message.* events for the private channel.

function FollowMe.MultiBox.AttachMessageEvents()
  if Event == nil or Event.System == nil then return end

  -- Note: RIFT uses Event.Message.Tell or a similar event for received messages.
  -- Actual event name needs in-game verification. Attaching defensively.
  local ok, err = pcall(function()
    if Event.Message ~= nil and Event.Message.Tell ~= nil then
      Command.Event.Attach(
        Event.Message.Tell,
        function(msgType, senderName, channelId, data)
          FollowMe.MultiBox.OnMessageReceived(msgType, senderName, channelId, data)
        end,
        "FollowMe.MultiBox.OnMessageReceived"
      )
    end
  end)
  if not ok then
    FollowMe.Diagnostics.Log("MultiBox: Could not attach message event: " .. tostring(err))
  end
end

-- ── Leader: broadcast target name to follower ─────────────────────────────────
-- Call this when the leader targets something new. The C# screen channel handles
-- the 10-char target name hash; this provides the full name via private message.

function FollowMe.MultiBox.BroadcastTargetToFollower(followerName, targetName)
  if _mode ~= "leader" then return end
  if Command == nil or Command.Message == nil or Command.Message.Broadcast == nil then return end

  local ok, err = pcall(function()
    Command.Message.Broadcast("tell", followerName, config.multiBoxChannelId, "target:" .. (targetName or ""))
  end)

  if not ok then
    FollowMe.Diagnostics.Log("MultiBox: Broadcast failed: " .. tostring(err))
  end
end

-- FollowMe.MultiBox | v0.1.0 | END
