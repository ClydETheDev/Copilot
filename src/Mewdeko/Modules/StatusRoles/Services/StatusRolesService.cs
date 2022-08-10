namespace Mewdeko.Modules.StatusRoles.Services;

public class StatusRolesService : INService
{
    private readonly DiscordShardedClient _client;
    public readonly DbService _db;

    public StatusRolesService(DiscordShardedClient client, DbService db)
    {
        _client = client;
        _db = db;
    }
}