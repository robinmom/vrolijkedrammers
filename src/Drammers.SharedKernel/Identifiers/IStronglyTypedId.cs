namespace Drammers.SharedKernel.Identifiers;

/// <summary>
/// Marker voor strongly-typed ID's (bijv. <c>readonly record struct MemberId(Guid Value) : IStronglyTypedId</c>),
/// zodat ID's van verschillende entiteiten niet door elkaar gebruikt kunnen worden (docs/03 §5).
/// </summary>
public interface IStronglyTypedId
{
    Guid Value { get; }
}
