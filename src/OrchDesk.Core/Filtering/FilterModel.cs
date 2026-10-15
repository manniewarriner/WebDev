namespace OrchDesk.Core.Filtering;

public enum FilterOperator { Is, IsNot, Contains }
public sealed record FilterModel(string Field, FilterOperator Operator, string Value)
{
    public bool Matches(string? candidate) => Operator switch { FilterOperator.Is => string.Equals(candidate, Value, StringComparison.OrdinalIgnoreCase), FilterOperator.IsNot => !string.Equals(candidate, Value, StringComparison.OrdinalIgnoreCase), _ => candidate?.Contains(Value, StringComparison.OrdinalIgnoreCase) == true };
    public static Func<T, bool> BuildPredicate<T>(IEnumerable<FilterModel> filters, Func<T, string, string?> field) { var snapshot = filters.ToArray(); return item => snapshot.All(f => f.Matches(field(item, f.Field))); }
}
