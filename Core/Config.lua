-- FollowMe Config | v0.1.0 | 1,865 chars
FollowMe = FollowMe or {}

local function Rgba(r, g, b)
  return { r / 255, g / 255, b / 255, 1.0 }
end
FollowMe.Config = {
  addonIdentifier = "FollowMe",
  addonVersion = "0.1.0",
  requestedLayer = 100000,
  requestedStrata = "topmost",
  showOnStartup = true,
  refreshIntervalSeconds = 0.05,
  statsPageFrameInterval = 2,
  statsSnapshotRefreshSeconds = 0.50,
  statsPageCount = 5,
  protocolVersion = 1,
  -- Frame type identifiers (must match C# FrameType enum)
  frameTypes = {
    core = 1,
    playerStatsPage = 2,
    playerPosition = 3,
    multiBoxState = 4
  },
  -- MultiBox frame rotation: how often position/state frames are interleaved
  -- with existing CoreStatus / PlayerStats frames (every N frames in the sequence)
  multiBoxPositionInterval = 3,   -- send position every 3rd frame
  multiBoxStateInterval = 6,      -- send multibox state every 6th frame
  -- MultiBox addon channel identifier for Command.Message.Broadcast
  -- Uses "tell" type addressed to specific character names (does not appear in chat)
  multiBoxChannelId = "FollowMe.MultiBox",
  syntheticMode = {
    enabled = false
  },
  profile = {
    id = "P360C",
    numericId = 1,
    windowWidth = 640,
    windowHeight = 360,
    bandWidth = 640,
    bandHeight = 24,
    segmentCount = 80,
    segmentWidth = 8,
    segmentHeight = 24,
    payloadStartIndex = 9,
    payloadSymbolCount = 64,
    displayScaleX = 1.0,
    displayScaleY = 1.0
  },
  controlLeft = { 0, 1, 0, 1, 2, 3, 4, 5 },
  controlRight = { 5, 4, 3, 2, 1, 0, 1, 0 },
  palette = {
    [0] = Rgba(16, 16, 16),
    [1] = Rgba(245, 245, 245),
    [2] = Rgba(255, 59, 48),
    [3] = Rgba(52, 199, 89),
    [4] = Rgba(10, 132, 255),
    [5] = Rgba(255, 214, 10),
    [6] = Rgba(191, 90, 242),
    [7] = Rgba(100, 210, 255)
  },
  resourceKinds = {
    none = 0,
    mana = 1,
    energy = 2,
    power = 3,
    charge = 4,
    planar = 5
  },
  callingCodes = {
    warrior = 1,
    cleric = 2,
    mage = 3,
    rogue = 4,
    primalist = 5
  },
  roleCodes = {
    unknown = 0,
    dps = 1,
    tank = 2,
    healer = 3,
    support = 4
  },
  relationCodes = {
    unknown = 0,
    friendly = 1,
    hostile = 2,
    neutral = 3,
    self = 4
  }
}

function FollowMe.Config.GetPaletteColor(symbol)
  return FollowMe.Config.palette[symbol] or FollowMe.Config.palette[0]
end

-- FollowMe Config | v0.1.0 | END
