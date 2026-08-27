using System.Linq.Expressions;

namespace Sts2LanConnect.Scripts;

// 通过成员名按表达式树构建玩家访问器。
// 编译产物中不存在引用具体游戏玩家类型的 TypeDef 字段/基类/接口，
// 从而保证同一个 mod 程序集在 0.107.1 与 0.111.0 上都能通过 Assembly.GetTypes() 加载。
internal sealed class LanConnectTailPlayerAccessors<TPlayer>
{
    public required Func<TPlayer, ulong> GetId { get; init; }

    public required Func<TPlayer, int>? GetSlotId { get; init; }

    public required Action<TPlayer, int> SetSlotId { get; init; }

    public static LanConnectTailPlayerAccessors<TPlayer> FromMembers(string idMember, string? slotMember)
    {
        ParameterExpression player = Expression.Parameter(typeof(TPlayer), "player");
        Func<TPlayer, ulong> getId = Expression.Lambda<Func<TPlayer, ulong>>(
            RequireMember(player, idMember),
            player).Compile();

        MemberExpression? slotExpression = slotMember == null ? null : ResolveMemberOrNull(player, slotMember);
        Func<TPlayer, int>? getSlotId = slotExpression == null
            ? null
            : Expression.Lambda<Func<TPlayer, int>>(slotExpression, player).Compile();
        Action<TPlayer, int> setSlotId = BuildSetSlotId(slotExpression, player);
        return new LanConnectTailPlayerAccessors<TPlayer>
        {
            GetId = getId,
            GetSlotId = getSlotId,
            SetSlotId = setSlotId,
        };
    }

    private static Expression RequireMember(ParameterExpression player, string memberName)
    {
        return ResolveMemberOrNull(player, memberName)
            ?? throw new InvalidOperationException(
                $"{typeof(TPlayer).FullName} has no public instance member '{memberName}'.");
    }

    private static MemberExpression? ResolveMemberOrNull(ParameterExpression player, string memberName)
    {
        System.Reflection.PropertyInfo? property =
            typeof(TPlayer).GetProperty(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property != null)
        {
            return Expression.Property(player, property);
        }

        System.Reflection.FieldInfo? field =
            typeof(TPlayer).GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return field == null ? null : Expression.Field(player, field);
    }

    private static Action<TPlayer, int> BuildSetSlotId(MemberExpression? slotExpression, ParameterExpression player)
    {
        if (slotExpression?.Member is System.Reflection.PropertyInfo { CanWrite: true } writableProperty)
        {
            ParameterExpression value = Expression.Parameter(typeof(int), "slot");
            return Expression.Lambda<Action<TPlayer, int>>(
                Expression.Assign(Expression.Property(player, writableProperty), value),
                player,
                value).Compile();
        }

        if (slotExpression?.Member is System.Reflection.FieldInfo
            {
                IsInitOnly: false, IsLiteral: false
            } writableField)
        {
            ParameterExpression value = Expression.Parameter(typeof(int), "slot");
            return Expression.Lambda<Action<TPlayer, int>>(
                Expression.Assign(Expression.Field(player, writableField), value),
                player,
                value).Compile();
        }

        return static (_, _) => throw new NotSupportedException(
            $"Slot member on {typeof(TPlayer).FullName} is read-only or absent.");
    }
}
