namespace IPAlloc.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
internal sealed class AllowAttribute : Attribute
{
    public AllowAttribute(params string[] roles)
    {
        Roles = roles;
    }

    public string[] Roles { get; }
}