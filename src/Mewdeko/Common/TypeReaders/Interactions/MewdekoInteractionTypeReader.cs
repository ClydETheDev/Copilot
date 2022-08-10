using Discord.Interactions;

// ReSharper disable UnusedTypeParameter

namespace Mewdeko.Common.TypeReaders.Interactions;

public abstract class MewdekoTypeReader<T> : TypeReader
{
    // ReSharper disable once NotAccessedField.Local
    private readonly DiscordShardedClient _client;
    // ReSharper disable once NotAccessedField.Local
    private readonly InteractionService _cmds;

    protected MewdekoTypeReader(DiscordShardedClient client, InteractionService cmds)
    {
        _client = client;
        _cmds = cmds;
    }
}
