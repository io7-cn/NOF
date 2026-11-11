using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Domain;

/// <summary>Connects shared failure values to domain exceptions without reversing layer dependencies.</summary>
public static partial class NOFDomainExtensions
{
    extension(Failure failure)
    {
        /// <summary>Throws a domain exception carrying this failure's code and message.</summary>
        [DoesNotReturn]
        public void Throw() => throw new DomainException(failure);
    }
}
