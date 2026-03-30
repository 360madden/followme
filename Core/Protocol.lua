-- FollowMe Protocol | v0.3.0
-- Research findings (2026-03-30):
--   Inspect.Unit.Detail("player").coordX / .coordY / .coordZ — VERIFIED (seebs.net live API)
--   Command.Message.Broadcast("tell", targetName, id, data) — VERIFIED (private, not in chat)
--   Event.System.Update.Begin — VERIFIED (fires every frame, reliable during movement/combat)
--   Player facing/yaw — NOT EXPOSED in RIFT API; bearing calculated from position delta in C#
FollowMe = FollowMe or {}
FollowMe.Protocol = {}

local config = FollowMe.Config
local frameTypes = config.frameTypes or { core = 1, playerStatsPage = 2, playerPosition = 3, multiBoxState = 4 }
local statsPageSchemas = {
  vitals = 1,
  main = 2,
  offense = 3,
  defense = 4,
  resist = 5
}

local function ClampByte(value)
  local number = tonumber(value) or 0
  if number < 0 then
    return 0
  end
  if number > 255 then
    return 255
  end
  return math.floor(number + 0.5)
end

local function ClampUInt16(value)
  local number = tonumber(value) or 0
  if number < 0 then
    return 0
  end
  if number > 65535 then
    return 65535
  end
  return math.floor(number + 0.5)
end

local function ClampUInt32(value)
  local number = tonumber(value) or 0
  if number < 0 then
    return 0
  end
  if number > 4294967295 then
    return 4294967295
  end
  return math.floor(number + 0.5)
end

local function Band(left, right)
  local result = 0
  local bitValue = 1
  local a = math.floor(left or 0)
  local b = math.floor(right or 0)

  while a > 0 or b > 0 do
    local abit = math.fmod(a, 2)
    local bbit = math.fmod(b, 2)
    if abit == 1 and bbit == 1 then
      result = result + bitValue
    end
    a = math.floor(a / 2)
    b = math.floor(b / 2)
    bitValue = bitValue * 2
  end

  return result
end

local function Bxor(left, right)
  local result = 0
  local bitValue = 1
  local a = math.floor(left or 0)
  local b = math.floor(right or 0)

  while a > 0 or b > 0 do
    local abit = math.fmod(a, 2)
    local bbit = math.fmod(b, 2)
    if abit ~= bbit then
      result = result + bitValue
    end
    a = math.floor(a / 2)
    b = math.floor(b / 2)
    bitValue = bitValue * 2
  end

  return result
end

local function Lshift(value, bits)
  return math.fmod((math.floor(value or 0) * (2 ^ bits)), 4294967296)
end

local function Rshift(value, bits)
  return math.floor((math.floor(value or 0)) / (2 ^ bits))
end

local function ComputeCrc16(bytes, firstIndex, lastIndex)
  local crc = 65535
  local index
  local loop

  for index = firstIndex, lastIndex do
    crc = Bxor(crc, Lshift(bytes[index], 8))
    for loop = 1, 8 do
      if Band(crc, 32768) ~= 0 then
        crc = Band(Bxor(Lshift(crc, 1), 4129), 65535)
      else
        crc = Band(Lshift(crc, 1), 65535)
      end
    end
  end

  return crc
end

local function ComputeCrc32C(bytes)
  local crc = 4294967295
  local index
  local loop

  for index = 1, #bytes do
    crc = Bxor(crc, bytes[index])
    for loop = 1, 8 do
      if Band(crc, 1) ~= 0 then
        crc = Bxor(Rshift(crc, 1), 2197175160)
      else
        crc = Rshift(crc, 1)
      end
    end
  end

  return Bxor(crc, 4294967295)
end

local function AppendBigEndian16(bytes, offset, value)
  local clamped = ClampUInt16(value)
  bytes[offset] = math.floor(clamped / 256)
  bytes[offset + 1] = math.fmod(clamped, 256)
end

local function AppendBigEndian32(bytes, offset, value)
  local clamped = ClampUInt32(value)
  bytes[offset] = math.floor(clamped / 16777216)
  bytes[offset + 1] = math.floor(math.fmod(clamped, 16777216) / 65536)
  bytes[offset + 2] = math.floor(math.fmod(clamped, 65536) / 256)
  bytes[offset + 3] = math.fmod(clamped, 256)
end

local function BuildFrame(payload, frameType, schemaId, sequence)
  local bytes = {}
  local headerCrc
  local payloadCrc
  local payloadSymbols
  local index

  bytes[1] = string.byte("C")
  bytes[2] = string.byte("L")
  bytes[3] = (config.protocolVersion * 16) + config.profile.numericId
  bytes[4] = (frameType * 16) + schemaId
  bytes[5] = ClampByte(sequence)
  bytes[6] = 0

  headerCrc = ComputeCrc16(bytes, 1, 6)
  AppendBigEndian16(bytes, 7, headerCrc)

  for index = 1, 12 do
    bytes[8 + index] = ClampByte(payload[index] or 0)
  end

  payloadCrc = ComputeCrc32C(payload)
  AppendBigEndian32(bytes, 21, payloadCrc)
  payloadSymbols = FollowMe.Protocol.EncodeBytesToSymbols(bytes)

  return bytes, FollowMe.Protocol.ComposeSegmentSymbols(payloadSymbols)
end

function FollowMe.Protocol.EncodeBytesToSymbols(bytes)
  local symbols = {}
  local symbolIndex
  local bitIndex

  for symbolIndex = 0, 63 do
    local symbol = 0
    for bitIndex = 0, 2 do
      local streamBit = (symbolIndex * 3) + bitIndex
      local byteIndex = math.floor(streamBit / 8) + 1
      local bitInByte = 7 - math.fmod(streamBit, 8)
      local bit = Band(Rshift(bytes[byteIndex], bitInByte), 1)
      symbol = (symbol * 2) + bit
    end
    symbols[symbolIndex + 1] = symbol
  end

  return symbols
end

function FollowMe.Protocol.ComposeSegmentSymbols(payloadSymbols)
  local symbols = {}
  local index

  for index = 1, #config.controlLeft do
    symbols[index] = config.controlLeft[index]
  end

  for index = 1, #payloadSymbols do
    symbols[8 + index] = payloadSymbols[index]
  end

  for index = 1, #config.controlRight do
    symbols[72 + index] = config.controlRight[index]
  end

  return symbols
end

function FollowMe.Protocol.BuildCoreFrame(snapshot, sequence)
  local payload = {
    ClampByte(snapshot.playerStateFlags),
    ClampByte(snapshot.playerHealthPctQ8),
    ClampByte(snapshot.playerResourceKind),
    ClampByte(snapshot.playerResourcePctQ8),
    ClampByte(snapshot.targetStateFlags),
    ClampByte(snapshot.targetHealthPctQ8),
    ClampByte(snapshot.targetResourceKind),
    ClampByte(snapshot.targetResourcePctQ8),
    ClampByte(snapshot.playerLevel),
    ClampByte(snapshot.targetLevel),
    ClampByte(snapshot.playerCallingRolePacked),
    ClampByte(snapshot.targetCallingRelationPacked)
  }

  return BuildFrame(payload, frameTypes.core, 1, sequence)
end

-- ── MultiBox frame helpers ────────────────────────────────────────────────────

-- Encode a signed float as a big-endian 4-byte int32 (×100 fixed point).
-- Negative values are stored as two's-complement uint32 (subtract from 2^32).
-- Range: ±21,474,836 world units at ×100 scale.
local function FloatToFixed(value)
  local number = tonumber(value) or 0
  local scaled = math.floor(number * 100 + 0.5)
  if scaled < 0 then
    -- Two's complement: add 2^32 to represent negative as unsigned
    scaled = scaled + 4294967296
  end
  return ClampUInt32(scaled)
end

-- Clamp and encode a single ASCII character byte (0 for nil/out-of-range)
local function SafeCharByte(str, index)
  if str == nil or index > #str then
    return 0
  end
  local b = string.byte(str, index)
  if b == nil or b < 32 or b > 126 then
    return 0
  end
  return b
end

function FollowMe.Protocol.BuildPlayerPositionFrame(snapshot, sequence)
  -- snapshot: { x, y, z } — world coordinates from Inspect.Unit.Detail("player")
  -- NilRisk: snapshot fields default to 0 via FloatToFixed if nil
  local payload = {}
  AppendBigEndian32(payload, 1, FloatToFixed(snapshot and snapshot.x or 0))
  AppendBigEndian32(payload, 5, FloatToFixed(snapshot and snapshot.y or 0))
  AppendBigEndian32(payload, 9, FloatToFixed(snapshot and snapshot.z or 0))
  return BuildFrame(payload, frameTypes.playerPosition, 1, sequence)
end

function FollowMe.Protocol.BuildMultiBoxStateFrame(snapshot, sequence)
  -- snapshot: { inCombat, hasTarget, targetHostile, targetName }
  -- NilRisk: all fields nil-safe with defaults
  local flags = 0
  if snapshot and snapshot.inCombat then flags = flags + 1 end
  if snapshot and snapshot.hasTarget then flags = flags + 2 end
  if snapshot and snapshot.targetHostile then flags = flags + 4 end

  local targetName = (snapshot and snapshot.targetName) or ""
  local nameLen = math.min(#targetName, 10)

  local payload = {}
  payload[1] = ClampByte(flags)
  payload[2] = ClampByte(nameLen)
  local i
  for i = 1, 10 do
    payload[2 + i] = SafeCharByte(targetName, i)
  end

  return BuildFrame(payload, frameTypes.multiBoxState, 1, sequence)
end

-- ─────────────────────────────────────────────────────────────────────────────

function FollowMe.Protocol.BuildPlayerStatsPageFrame(snapshot, sequence, pageSchemaId)
  local payload = {}

  if pageSchemaId == statsPageSchemas.vitals then
    AppendBigEndian32(payload, 1, snapshot.vitals.healthCurrent)
    AppendBigEndian32(payload, 5, snapshot.vitals.healthMax)
    AppendBigEndian16(payload, 9, snapshot.vitals.resourceCurrent)
    AppendBigEndian16(payload, 11, snapshot.vitals.resourceMax)
  elseif pageSchemaId == statsPageSchemas.main then
    AppendBigEndian16(payload, 1, snapshot.main.armor)
    AppendBigEndian16(payload, 3, snapshot.main.strength)
    AppendBigEndian16(payload, 5, snapshot.main.dexterity)
    AppendBigEndian16(payload, 7, snapshot.main.intelligence)
    AppendBigEndian16(payload, 9, snapshot.main.wisdom)
    AppendBigEndian16(payload, 11, snapshot.main.endurance)
  elseif pageSchemaId == statsPageSchemas.offense then
    AppendBigEndian16(payload, 1, snapshot.offense.attackPower)
    AppendBigEndian16(payload, 3, snapshot.offense.physicalCrit)
    AppendBigEndian16(payload, 5, snapshot.offense.hit)
    AppendBigEndian16(payload, 7, snapshot.offense.spellPower)
    AppendBigEndian16(payload, 9, snapshot.offense.spellCrit)
    AppendBigEndian16(payload, 11, snapshot.offense.critPower)
  elseif pageSchemaId == statsPageSchemas.defense then
    AppendBigEndian16(payload, 1, snapshot.defense.dodge)
    AppendBigEndian16(payload, 3, snapshot.defense.block)
    AppendBigEndian16(payload, 5, snapshot.defense.reserved1)
    AppendBigEndian16(payload, 7, snapshot.defense.reserved2)
    AppendBigEndian16(payload, 9, snapshot.defense.reserved3)
    AppendBigEndian16(payload, 11, snapshot.defense.reserved4)
  elseif pageSchemaId == statsPageSchemas.resist then
    AppendBigEndian16(payload, 1, snapshot.resist.life)
    AppendBigEndian16(payload, 3, snapshot.resist.death)
    AppendBigEndian16(payload, 5, snapshot.resist.fire)
    AppendBigEndian16(payload, 7, snapshot.resist.water)
    AppendBigEndian16(payload, 9, snapshot.resist.earth)
    AppendBigEndian16(payload, 11, snapshot.resist.air)
  else
    error("Unsupported player stats page schema: " .. tostring(pageSchemaId))
  end

  return BuildFrame(payload, frameTypes.playerStatsPage, pageSchemaId, sequence)
end
