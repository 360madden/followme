-- FollowMe Gather | v0.1.0 | 10,589 chars
FollowMe = FollowMe or {}
FollowMe.Gather = {}

local config = FollowMe.Config

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

local function Lower(value)
  if value == nil then
    return ""
  end
  return string.lower(tostring(value))
end

local function SafeUnitLookup(reference)
  if Inspect == nil or Inspect.Unit == nil or Inspect.Unit.Lookup == nil then
    return nil
  end

  local ok, result = pcall(Inspect.Unit.Lookup, reference)
  if ok then
    return result
  end

  return nil
end

local function SafeUnitDetail(unit)
  if unit == nil or Inspect == nil or Inspect.Unit == nil or Inspect.Unit.Detail == nil then
    return nil
  end

  local ok, result = pcall(Inspect.Unit.Detail, unit)
  if ok then
    return result
  end

  return nil
end

local function SafeInspectStats()
  if Inspect == nil or Inspect.Stat == nil then
    return nil
  end

  local ok, result = pcall(Inspect.Stat)
  if ok then
    return result
  end

  return nil
end

local function QuantizePercent(current, maximum)
  local currentNumber = tonumber(current) or 0
  local maxNumber = tonumber(maximum) or 0

  if maxNumber <= 0 then
    return 0
  end

  return ClampByte((currentNumber / maxNumber) * 255)
end

local function EncodeCallingCode(value)
  local text = Lower(value)
  if string.find(text, "warrior", 1, true) then
    return config.callingCodes.warrior
  end
  if string.find(text, "cleric", 1, true) then
    return config.callingCodes.cleric
  end
  if string.find(text, "mage", 1, true) then
    return config.callingCodes.mage
  end
  if string.find(text, "rogue", 1, true) then
    return config.callingCodes.rogue
  end
  if string.find(text, "primalist", 1, true) then
    return config.callingCodes.primalist
  end
  return 0
end

local function EncodeRoleCode(value)
  local text = Lower(value)
  if string.find(text, "tank", 1, true) then
    return config.roleCodes.tank
  end
  if string.find(text, "heal", 1, true) then
    return config.roleCodes.healer
  end
  if string.find(text, "support", 1, true) then
    return config.roleCodes.support
  end
  if string.find(text, "dps", 1, true) or string.find(text, "damage", 1, true) then
    return config.roleCodes.dps
  end
  return config.roleCodes.unknown
end

local function EncodeRelationCode(value, targetUnitId)
  if targetUnitId == "player" then
    return config.relationCodes.self
  end

  local text = Lower(value)
  if string.find(text, "hostile", 1, true) or string.find(text, "enemy", 1, true) then
    return config.relationCodes.hostile
  end
  if string.find(text, "friendly", 1, true) or string.find(text, "ally", 1, true) then
    return config.relationCodes.friendly
  end
  if string.find(text, "neutral", 1, true) then
    return config.relationCodes.neutral
  end
  return config.relationCodes.unknown
end

local function SelectPreferredResourceSnapshot(detail, callingCode)
  if detail == nil then
    return config.resourceKinds.none, 0, 0, 0
  end

  local candidates = {
    { kind = config.resourceKinds.mana, current = detail.mana, maximum = detail.manaMax },
    { kind = config.resourceKinds.energy, current = detail.energy, maximum = detail.energyMax },
    { kind = config.resourceKinds.power, current = detail.power, maximum = detail.powerMax },
    { kind = config.resourceKinds.charge, current = detail.charge, maximum = detail.chargeMax },
    { kind = config.resourceKinds.planar, current = detail.planar, maximum = detail.planarMax }
  }

  local preferredKind = config.resourceKinds.none
  if callingCode == config.callingCodes.rogue then
    preferredKind = config.resourceKinds.energy
  elseif callingCode == config.callingCodes.warrior then
    preferredKind = config.resourceKinds.power
  else
    preferredKind = config.resourceKinds.mana
  end

  local index
  for index = 1, #candidates do
    local candidate = candidates[index]
    if candidate.kind == preferredKind and tonumber(candidate.maximum) ~= nil and tonumber(candidate.maximum) > 0 then
      return candidate.kind, ClampUInt16(candidate.current), ClampUInt16(candidate.maximum), QuantizePercent(candidate.current, candidate.maximum)
    end
  end

  for index = 1, #candidates do
    local candidate = candidates[index]
    if tonumber(candidate.maximum) ~= nil and tonumber(candidate.maximum) > 0 then
      return candidate.kind, ClampUInt16(candidate.current), ClampUInt16(candidate.maximum), QuantizePercent(candidate.current, candidate.maximum)
    end
  end

  return config.resourceKinds.none, 0, 0, 0
end

local function BuildPlayerStateFlags(detail)
  local flags = 0
  if detail ~= nil then
    flags = flags + 1
  end
  if detail ~= nil and not detail.dead then
    flags = flags + 2
  end
  if detail ~= nil and detail.combat then
    flags = flags + 4
  end
  return flags
end

local function BuildTargetStateFlags(detail)
  local flags = 0
  if detail ~= nil then
    flags = flags + 1
  end
  if detail ~= nil and not detail.dead then
    flags = flags + 2
  end
  if detail ~= nil and detail.combat then
    flags = flags + 4
  end
  if detail ~= nil and (detail.tagged or detail.marked) then
    flags = flags + 8
  end
  return flags
end

local function GetStatValue(stats, key)
  if stats == nil then
    return 0
  end
  return ClampUInt16(stats[key])
end

function FollowMe.Gather.BuildCoreStatusSnapshot()
  local player = SafeUnitDetail("player")
  local targetUnitId = SafeUnitLookup("player.target")
  local target = SafeUnitDetail(targetUnitId)
  local playerCallingCode = EncodeCallingCode(player and (player.calling or player.callingName))
  local targetCallingCode = EncodeCallingCode(target and (target.calling or target.callingName))
  local playerRoleCode = EncodeRoleCode(player and (player.role or player.roleName or player.playstyle))
  local relationCode = EncodeRelationCode(target and target.relation, targetUnitId)
  local playerResourceKind, _, _, playerResourcePct = SelectPreferredResourceSnapshot(player, playerCallingCode)
  local targetResourceKind, _, _, targetResourcePct = SelectPreferredResourceSnapshot(target, targetCallingCode)

  return {
    playerStateFlags = ClampByte(BuildPlayerStateFlags(player)),
    playerHealthPctQ8 = ClampByte(QuantizePercent(player and player.health, player and player.healthMax)),
    playerResourceKind = ClampByte(playerResourceKind),
    playerResourcePctQ8 = ClampByte(playerResourcePct),
    targetStateFlags = ClampByte(BuildTargetStateFlags(target)),
    targetHealthPctQ8 = ClampByte(QuantizePercent(target and target.health, target and target.healthMax)),
    targetResourceKind = ClampByte(targetResourceKind),
    targetResourcePctQ8 = ClampByte(targetResourcePct),
    playerLevel = ClampByte(player and player.level),
    targetLevel = ClampByte(target and target.level),
    playerCallingRolePacked = ClampByte((playerCallingCode * 16) + playerRoleCode),
    targetCallingRelationPacked = ClampByte((targetCallingCode * 16) + relationCode)
  }
end

function FollowMe.Gather.BuildPlayerStatsSnapshot()
  local player = SafeUnitDetail("player")
  local playerCallingCode = EncodeCallingCode(player and (player.calling or player.callingName))
  local playerResourceKind, resourceCurrent, resourceMax, _ = SelectPreferredResourceSnapshot(player, playerCallingCode)
  local stats = SafeInspectStats()

  return {
    resourceKind = ClampByte(playerResourceKind),
    vitals = {
      healthCurrent = ClampUInt32(player and player.health),
      healthMax = ClampUInt32(player and player.healthMax),
      resourceCurrent = ClampUInt16(resourceCurrent),
      resourceMax = ClampUInt16(resourceMax)
    },
    main = {
      armor = GetStatValue(stats, "armor"),
      strength = GetStatValue(stats, "strength"),
      dexterity = GetStatValue(stats, "dexterity"),
      intelligence = GetStatValue(stats, "intelligence"),
      wisdom = GetStatValue(stats, "wisdom"),
      endurance = GetStatValue(stats, "endurance")
    },
    offense = {
      attackPower = GetStatValue(stats, "powerAttack"),
      physicalCrit = GetStatValue(stats, "critAttack"),
      hit = GetStatValue(stats, "hit"),
      spellPower = GetStatValue(stats, "powerSpell"),
      spellCrit = GetStatValue(stats, "critSpell"),
      critPower = GetStatValue(stats, "critPower")
    },
    defense = {
      dodge = GetStatValue(stats, "dodge"),
      block = GetStatValue(stats, "block"),
      reserved1 = 0,
      reserved2 = 0,
      reserved3 = 0,
      reserved4 = 0
    },
    resist = {
      life = GetStatValue(stats, "resistLife"),
      death = GetStatValue(stats, "resistDeath"),
      fire = GetStatValue(stats, "resistFire"),
      water = GetStatValue(stats, "resistWater"),
      earth = GetStatValue(stats, "resistEarth"),
      air = GetStatValue(stats, "resistAir")
    }
  }
end

function FollowMe.Gather.BuildSyntheticCoreStatusSnapshot()
  return {
    playerStateFlags = 7,
    playerHealthPctQ8 = 198,
    playerResourceKind = config.resourceKinds.mana,
    playerResourcePctQ8 = 144,
    targetStateFlags = 15,
    targetHealthPctQ8 = 91,
    targetResourceKind = config.resourceKinds.none,
    targetResourcePctQ8 = 0,
    playerLevel = 70,
    targetLevel = 72,
    playerCallingRolePacked = 49,
    targetCallingRelationPacked = 66
  }
end

function FollowMe.Gather.BuildSyntheticPlayerStatsSnapshot()
  return {
    resourceKind = config.resourceKinds.energy,
    vitals = {
      healthCurrent = 3260,
      healthMax = 3260,
      resourceCurrent = 100,
      resourceMax = 100
    },
    main = {
      armor = 660,
      strength = 62,
      dexterity = 102,
      intelligence = 16,
      wisdom = 19,
      endurance = 98
    },
    offense = {
      attackPower = 103,
      physicalCrit = 112,
      hit = 1,
      spellPower = 17,
      spellCrit = 17,
      critPower = 11
    },
    defense = {
      dodge = 102,
      block = 0,
      reserved1 = 0,
      reserved2 = 0,
      reserved3 = 0,
      reserved4 = 0
    },
    resist = {
      life = 36,
      death = 56,
      fire = 36,
      water = 36,
      earth = 36,
      air = 36
    }
  }
end

-- FollowMe Gather | v0.1.0 | END
