FollowMe = FollowMe or {}
FollowMe.ErrorTrap = {
  lastErrorId = nil
}

function FollowMe.ErrorTrap.OnSystemError(_, errorData)
  if errorData == nil or errorData.addon ~= FollowMe.Config.addonIdentifier then
    return
  end

  if errorData.id ~= nil and errorData.id == FollowMe.ErrorTrap.lastErrorId then
    return
  end

  FollowMe.ErrorTrap.lastErrorId = errorData.id
  FollowMe.Diagnostics.Log("Addon error: " .. tostring(errorData.message))
end

Command.Event.Attach(
  Event.System.Error,
  FollowMe.ErrorTrap.OnSystemError,
  "FollowMe.ErrorTrap.OnSystemError"
)
