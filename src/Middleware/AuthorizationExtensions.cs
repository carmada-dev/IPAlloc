using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using IPAlloc.Authorization;

using Microsoft.Azure.Functions.Worker;

namespace IPAlloc.Middleware;
internal static class AuthorizationExtensions
{
    private static readonly ConcurrentDictionary<string, MethodInfo?> FunctionMethodCache = new ConcurrentDictionary<string, MethodInfo?>();

    public static MethodInfo? GetFunctionMethod(this FunctionContext context)
    {
        var entryPoint = context.FunctionDefinition.EntryPoint;

        return FunctionMethodCache.GetOrAdd(entryPoint, _ => Assembly
            .LoadFrom(context.FunctionDefinition.PathToAssembly)
            .GetType(entryPoint.Substring(0, entryPoint.LastIndexOf('.')))?
            .GetMethod(entryPoint.Substring(entryPoint.LastIndexOf('.') + 1)));
    }

    public static IEnumerable<string> GetFunctionAllowRoles(this FunctionContext context)
    {
        var functionMethod = context.GetFunctionMethod();

        if (functionMethod is null)
            return Enumerable.Empty<string>();

        return functionMethod
            .GetCustomAttributes<AllowAttribute>()
            .SelectMany(attribute => attribute.Roles)
            .Union(functionMethod.DeclaringType?
                .GetCustomAttributes<AllowAttribute>()
                .SelectMany(attribute => attribute.Roles) ?? Enumerable.Empty<string>());
    }

    public static IEnumerable<string> GetFunctionDenyRoles(this FunctionContext context)
    {
        var functionMethod = context.GetFunctionMethod();

        if (functionMethod is null)
            return Enumerable.Empty<string>();

        return functionMethod
            .GetCustomAttributes<DenyAttribute>()
            .SelectMany(attribute => attribute.Roles)
            .Union(functionMethod.DeclaringType?
                .GetCustomAttributes<DenyAttribute>()
                .SelectMany(attribute => attribute.Roles) ?? Enumerable.Empty<string>());
    }
}
