using System.Linq.Expressions;
using System.Reflection;

namespace Meitou.FlowingHair;

/// <summary>
/// Compiled readers and writers of the renderer's non-public members (the port has no publicized references): a lambda compiled from an
/// expression tree skips visibility checks, and costs a field load per call.
/// </summary>
static class Access
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static Type Type(Assembly assembly, string name) =>
        assembly.GetType(name) ?? throw new MissingMemberException(name);

    public static Type Nested(Type outer, string name) =>
        outer.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMemberException(outer.FullName, name);

    public static MemberInfo Member(Type type, string name) =>
        (MemberInfo?)type.GetField(name, All) ?? type.GetProperty(name, All) ?? throw new MissingMemberException(type.FullName, name);

    /// <summary>A reader of the field or property <paramref name="name"/> of an object of <paramref name="type"/>, as <typeparamref name="T"/>.</summary>
    public static Func<object, T> Getter<T>(Type type, string name)
    {
        var target = Expression.Parameter(typeof(object));
        var member = Expression.MakeMemberAccess(Expression.Convert(target, type), Member(type, name));
        return Expression.Lambda<Func<object, T>>(Expression.Convert(member, typeof(T)), target).Compile();
    }

    /// <summary>A writer of the field <paramref name="name"/> of an object of <paramref name="type"/>.</summary>
    public static Action<object, T> Setter<T>(Type type, string name)
    {
        var target = Expression.Parameter(typeof(object));
        var value = Expression.Parameter(typeof(T));
        var member = Expression.MakeMemberAccess(Expression.Convert(target, type), Member(type, name));
        var assign = Expression.Assign(member, Expression.Convert(value, ((FieldInfo)Member(type, name)).FieldType));
        return Expression.Lambda<Action<object, T>>(assign, target, value).Compile();
    }

    /// <summary>The declared method <paramref name="name"/> of <paramref name="type"/> with <paramref name="parameters"/> parameters.</summary>
    public static MethodInfo Method(Type type, string name, int parameters) =>
        type.GetMethods(All | BindingFlags.DeclaredOnly).SingleOrDefault(m => m.Name == name && m.GetParameters().Length == parameters)
            ?? throw new MissingMethodException(type.FullName, name);
}
