namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the client value or one explicitly declared administration failure.</summary>
public union OAuthClientRegistration(OAuthClientRegistrationSecretDescriptor, OAuthClientNotFound, OAuthClientConflict, OAuthClientInvalidRequest, OAuthClientInvalidOperation, OAuthClientRegistrationTokenInvalid, OAuthClientMetadataInvalid, OAuthClientRedirectUriInvalid, OAuthClientStorageFailure);
