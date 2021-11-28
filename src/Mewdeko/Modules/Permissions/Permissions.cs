﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Extensions.Interactive;
using Mewdeko.Common.Extensions.Interactive.Entities.Page;
using Mewdeko.Common.Extensions.Interactive.Pagination;
using Mewdeko.Common.Extensions.Interactive.Pagination.Lazy;
using Mewdeko.Common.TypeReaders;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Services;
using Mewdeko.Services.Database.Models;
using Mewdeko.Modules.Permissions.Common;
using Mewdeko.Modules.Permissions.Services;

namespace Mewdeko.Modules.Permissions
{
    public partial class Permissions : MewdekoModuleBase<PermissionService>
    {
        public enum Reset
        {
            Reset
        }

        private readonly DbService _db;
        private InteractiveService Interactivity;
        public Permissions(DbService db, InteractiveService inter)
        {
            Interactivity = inter;
            _db = db;
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task Verbose(PermissionAction action = null)
        {
            using (var uow = _db.GetDbContext())
            {
                var config = uow.GuildConfigs.GcWithPermissionsv2For(ctx.Guild.Id);
                if (action == null)
                    action = new PermissionAction(!config.VerbosePermissions); // New behaviour, can toggle.
                config.VerbosePermissions = action.Value;
                await uow.SaveChangesAsync();
                Service.UpdateCache(config);
            }

            if (action.Value)
                await ReplyConfirmLocalizedAsync("verbose_true").ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("verbose_false").ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPermission.Administrator)]
        [Priority(0)]
        public async Task PermRole([Remainder] IRole role = null)
        {
            if (role != null && role == role.Guild.EveryoneRole)
                return;

            if (role == null)
            {
                var cache = Service.GetCacheFor(ctx.Guild.Id);
                if (!ulong.TryParse(cache.PermRole, out var roleId) ||
                    (role = ((SocketGuild)ctx.Guild).GetRole(roleId)) == null)
                    await ReplyConfirmLocalizedAsync("permrole_not_set", Format.Bold(cache.PermRole))
                        .ConfigureAwait(false);
                else
                    await ReplyConfirmLocalizedAsync("permrole", Format.Bold(role.ToString())).ConfigureAwait(false);
                return;
            }

            using (var uow = _db.GetDbContext())
            {
                var config = uow.GuildConfigs.GcWithPermissionsv2For(ctx.Guild.Id);
                config.PermissionRole = role.Id.ToString();
                uow.SaveChanges();
                Service.UpdateCache(config);
            }

            await ReplyConfirmLocalizedAsync("permrole_changed", Format.Bold(role.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPermission.Administrator)]
        [Priority(1)]
        public async Task PermRole(Reset _)
        {
            using (var uow = _db.GetDbContext())
            {
                var config = uow.GuildConfigs.GcWithPermissionsv2For(ctx.Guild.Id);
                config.PermissionRole = null;
                await uow.SaveChangesAsync();
                Service.UpdateCache(config);
            }

            await ReplyConfirmLocalizedAsync("permrole_reset").ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task ListPerms()
        {

            IList<Permissionv2> perms;

            if (Service.Cache.TryGetValue(ctx.Guild.Id, out var permCache))
                perms = permCache.Permissions.Source.ToList();
            else
                perms = Permissionv2.GetDefaultPermlist;
            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(perms.Count / 10)
                .WithDefaultEmotes()
                .Build();
            await Interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));
            Task<PageBuilder> PageFactory(int page)
            {
                var startPos = 20 * (page - 1);
                return Task.FromResult(new PageBuilder().WithDescription(string.Join("\n",
                                                                             perms
                                                                                 .Skip(page * 10)
                                                                                 .Take(10)
                                                                                 .Select(p =>
                                                                                 {
                                                                                     var str =
                                                                                         $"`{p.Index + 1}.` {Format.Bold(p.GetCommand(Prefix, (SocketGuild)ctx.Guild))}";
                                                                                     if (p.Index == 0)
                                                                                         str +=
                                                                                             $" [{GetText("uneditable")}]";
                                                                                     return str;
                                                                                 }))).WithTitle(Format.Bold(GetText("page", page + 1))).WithOkColor());

            }
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task RemovePerm(int index)
        {
            index -= 1;
            if (index < 0)
                return;
            try
            {
                Permissionv2 p;
                using (var uow = _db.GetDbContext())
                {
                    var config = uow.GuildConfigs.GcWithPermissionsv2For(ctx.Guild.Id);
                    var permsCol = new PermissionsCollection<Permissionv2>(config.Permissions);
                    p = permsCol[index];
                    permsCol.RemoveAt(index);
                    uow._context.Remove(p);
                    await uow.SaveChangesAsync();
                    Service.UpdateCache(config);
                }

                await ReplyConfirmLocalizedAsync("removed",
                    index + 1,
                    Format.Code(p.GetCommand(Prefix, (SocketGuild)ctx.Guild))).ConfigureAwait(false);
            }
            catch (IndexOutOfRangeException)
            {
                await ReplyErrorLocalizedAsync("perm_out_of_range").ConfigureAwait(false);
            }
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task MovePerm(int from, int to)
        {
            from -= 1;
            to -= 1;
            if (!(from == to || from < 0 || to < 0))
                try
                {
                    Permissionv2 fromPerm;
                    using (var uow = _db.GetDbContext())
                    {
                        var config = uow.GuildConfigs.GcWithPermissionsv2For(ctx.Guild.Id);
                        var permsCol = new PermissionsCollection<Permissionv2>(config.Permissions);

                        var fromFound = from < permsCol.Count;
                        var toFound = to < permsCol.Count;

                        if (!fromFound)
                        {
                            await ReplyErrorLocalizedAsync("not_found", ++from);
                            return;
                        }

                        if (!toFound)
                        {
                            await ReplyErrorLocalizedAsync("not_found", ++to);
                            return;
                        }

                        fromPerm = permsCol[from];

                        permsCol.RemoveAt(from);
                        permsCol.Insert(to, fromPerm);
                        await uow.SaveChangesAsync();
                        Service.UpdateCache(config);
                    }

                    await ReplyConfirmLocalizedAsync("moved_permission",
                            Format.Code(fromPerm.GetCommand(Prefix, (SocketGuild)ctx.Guild)),
                            ++from,
                            ++to)
                        .ConfigureAwait(false);
                    return;
                }
                catch (Exception e) when (e is ArgumentOutOfRangeException || e is IndexOutOfRangeException)
                {
                }

            await ReplyErrorLocalizedAsync("perm_out_of_range").ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task SrvrCmd(CommandOrCrInfo command, PermissionAction action)
        {
            
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Server,
                PrimaryTargetId = 0,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("sx_enable",
                    Format.Code(command.Name),
                    GetText("of_command")).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("sx_disable",
                    Format.Code(command.Name),
                    GetText("of_command")).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task SrvrMdl(ModuleOrCrInfo module, PermissionAction action)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Server,
                PrimaryTargetId = 0,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("sx_enable",
                    Format.Code(module.Name),
                    GetText("of_module")).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("sx_disable",
                    Format.Code(module.Name),
                    GetText("of_module")).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task UsrCmd(CommandOrCrInfo command, PermissionAction action, [Remainder] IGuildUser user)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.User,
                PrimaryTargetId = user.Id,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("ux_enable",
                    Format.Code(command.Name),
                    GetText("of_command"),
                    Format.Code(user.ToString())).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("ux_disable",
                    Format.Code(command.Name),
                    GetText("of_command"),
                    Format.Code(user.ToString())).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task UsrMdl(ModuleOrCrInfo module, PermissionAction action, [Remainder] IGuildUser user)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.User,
                PrimaryTargetId = user.Id,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("ux_enable",
                    Format.Code(module.Name),
                    GetText("of_module"),
                    Format.Code(user.ToString())).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("ux_disable",
                    Format.Code(module.Name),
                    GetText("of_module"),
                    Format.Code(user.ToString())).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task RoleCmd(CommandOrCrInfo command, PermissionAction action, [Remainder] IRole role)
        {
            if (role == role.Guild.EveryoneRole)
                return;

            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Role,
                PrimaryTargetId = role.Id,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("rx_enable",
                    Format.Code(command.Name),
                    GetText("of_command"),
                    Format.Code(role.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("rx_disable",
                    Format.Code(command.Name),
                    GetText("of_command"),
                    Format.Code(role.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task RoleMdl(ModuleOrCrInfo module, PermissionAction action, [Remainder] IRole role)
        {
            if (role == role.Guild.EveryoneRole)
                return;

            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Role,
                PrimaryTargetId = role.Id,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            }).ConfigureAwait(false);


            if (action.Value)
                await ReplyConfirmLocalizedAsync("rx_enable",
                    Format.Code(module.Name),
                    GetText("of_module"),
                    Format.Code(role.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("rx_disable",
                    Format.Code(module.Name),
                    GetText("of_module"),
                    Format.Code(role.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task ChnlCmd(CommandOrCrInfo command, PermissionAction action, [Remainder] ITextChannel chnl)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Channel,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("cx_enable",
                    Format.Code(command.Name),
                    GetText("of_command"),
                    Format.Code(chnl.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("cx_disable",
                    Format.Code(command.Name),
                    GetText("of_command"),
                    Format.Code(chnl.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task ChnlMdl(ModuleOrCrInfo module, PermissionAction action, [Remainder] ITextChannel chnl)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Channel,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("cx_enable",
                    Format.Code(module.Name),
                    GetText("of_module"),
                    Format.Code(chnl.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("cx_disable",
                    Format.Code(module.Name),
                    GetText("of_module"),
                    Format.Code(chnl.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task AllChnlMdls(PermissionAction action, [Remainder] ITextChannel chnl)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Channel,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = action.Value
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("acm_enable",
                    Format.Code(chnl.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("acm_disable",
                    Format.Code(chnl.Name)).ConfigureAwait(false);
        }
        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task CatCmd(CommandOrCrInfo command, PermissionAction action, [Remainder] ICategoryChannel chnl)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Category,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("cx_enable",
                    Format.Code(command.Name),
                    GetText("of_command"),
                    Format.Code(chnl.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("cx_disable",
                    Format.Code(command.Name),
                    GetText("of_command"),
                    Format.Code(chnl.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task CatMdl(ModuleOrCrInfo module, PermissionAction action, [Remainder] ICategoryChannel chnl)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Category,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("cx_enable",
                    Format.Code(module.Name),
                    GetText("of_module"),
                    Format.Code(chnl.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("cx_disable",
                    Format.Code(module.Name),
                    GetText("of_module"),
                    Format.Code(chnl.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task AllCatMdls(PermissionAction action, [Remainder] ICategoryChannel chnl)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Category,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = action.Value
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("acm_enable",
                    Format.Code(chnl.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("acm_disable",
                    Format.Code(chnl.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task AllRoleMdls(PermissionAction action, [Remainder] IRole role)
        {
            if (role == role.Guild.EveryoneRole)
                return;

            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Role,
                PrimaryTargetId = role.Id,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = action.Value
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("arm_enable",
                    Format.Code(role.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("arm_disable",
                    Format.Code(role.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task AllUsrMdls(PermissionAction action, [Remainder] IUser user)
        {
            await Service.AddPermissions(ctx.Guild.Id, new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.User,
                PrimaryTargetId = user.Id,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = action.Value
            }).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("aum_enable",
                    Format.Code(user.ToString())).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("aum_disable",
                    Format.Code(user.ToString())).ConfigureAwait(false);
        }

        [MewdekoCommand]
        [Usage]
        [Description]
        [Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task AllSrvrMdls(PermissionAction action)
        {
            var newPerm = new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Server,
                PrimaryTargetId = 0,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = action.Value
            };

            var allowUser = new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.User,
                PrimaryTargetId = ctx.User.Id,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = true
            };

            await Service.AddPermissions(ctx.Guild.Id,
                newPerm,
                allowUser).ConfigureAwait(false);

            if (action.Value)
                await ReplyConfirmLocalizedAsync("asm_enable").ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("asm_disable").ConfigureAwait(false);
        }
    }
}