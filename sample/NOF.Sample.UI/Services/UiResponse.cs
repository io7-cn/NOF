using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Sample.UI.Services;

/// <summary>Presentation outcome for a sample command after native HTTP adaptation.</summary>
public union UiResponse(Empty, Failure);

/// <summary>Presentation outcome for a sample resource after native HTTP adaptation.</summary>
public union UiResponse<T>(T, Failure);
