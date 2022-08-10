using Discord.Commands;
using Discord.Interactions;
using System.Threading.Tasks;

namespace Mewdeko.Common.ModuleBehaviors;

public interface ILateBlocker
{
    public int Priority { get; }

    Task<bool> TryBlockLate(DiscordShardedClient client, ICommandContext context,
        string moduleName, CommandInfo command);
    Task<bool> TryBlockLate(DiscordShardedClient client, IInteractionContext context,
        ICommandInfo command);
}