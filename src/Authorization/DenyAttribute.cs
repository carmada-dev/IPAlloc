namespace IPAlloc.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
internal sealed class DenyAttribute : Attribute
{
    public DenyAttribute(params string[] roles)
    {
        Roles = roles;
    }

    public string[] Roles { get; }
}