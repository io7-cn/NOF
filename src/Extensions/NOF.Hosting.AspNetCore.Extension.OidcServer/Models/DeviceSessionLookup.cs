using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the cache identities of a device session or its lookup failure.</summary>
public union DeviceSessionLookup((OidcDeviceUserCodeCacheKey UserCodeKey, OidcDeviceAuthorizationCacheKey DeviceCodeKey), Failure);
