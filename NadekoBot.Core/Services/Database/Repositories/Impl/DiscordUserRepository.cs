﻿using NadekoBot.Core.Services.Database.Models;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Discord;
using System.Collections.Generic;
using System;

namespace NadekoBot.Core.Services.Database.Repositories.Impl
{
    public class DiscordUserRepository : Repository<DiscordUser>, IDiscordUserRepository
    {
        public DiscordUserRepository(DbContext context) : base(context)
        {
        }

        public void EnsureCreated(ulong userId, string username, string discrim, string avatarId)
        {
            if (_context.Database.IsNpgsql())
            {
                _context.Database.ExecuteSqlCommand($@"
                                                        INSERT INTO ""DiscordUser"" (""UserId"", ""Username"", ""Discriminator"", ""AvatarId"")
                                                        VALUES ({userId}, {username}, {discrim}, {avatarId})
                                                        ON CONFLICT(""UserId"") DO UPDATE
                                                        SET ""Username""={username},
                                                        ""Discriminator""={discrim},
                                                        ""AvatarId""={avatarId};");
            }

            if (_context.Database.IsSqlServer())
            {
                _context.Database.ExecuteSqlCommand($@"
                                                    UPDATE DiscordUser
                                                    SET Username={username},
                                                    Discriminator={discrim},
                                                    AvatarId={avatarId}
                                                    WHERE UserId={userId}
                                                    IF @@ROWCOUNT=0
                                                        INSERT INTO DiscordUser (UserId, Username, Discriminator, AvatarId)
                                                        VALUES ({userId}, {username}, {discrim}, {avatarId});");
            }
        }

        //temp is only used in updatecurrencystate, so that i don't overwrite real usernames/discrims with Unknown
        public DiscordUser GetOrCreate(ulong userId, string username, string discrim, string avatarId)
        {
            EnsureCreated(userId, username, discrim, avatarId);
            return _set
                .Include(x => x.Club)
                .First(u => u.UserId == userId);
        }

        public DiscordUser GetOrCreate(IUser original)
            => GetOrCreate(original.Id, original.Username, original.Discriminator, original.AvatarId);

        public int GetUserGlobalRank(ulong id)
        {
            //            @"SELECT COUNT(*) + 1
            //FROM DiscordUser
            //WHERE TotalXp > COALESCE((SELECT TotalXp
            //    FROM DiscordUser
            //    WHERE UserId = @p1
            //    LIMIT 1), 0);"
            return _set.Where(x => x.TotalXp > (_set
                    .Where(y => y.UserId == id)
                    .Select(y => y.TotalXp)
                    .FirstOrDefault()))
                .Count() + 1;
        }

        public DiscordUser[] GetUsersXpLeaderboardFor(int page)
        {
            return _set
                .OrderByDescending(x => x.TotalXp)
                .Skip(page * 9)
                .Take(9)
                .AsEnumerable()
                .ToArray();
        }

        public List<DiscordUser> GetTopRichest(ulong botId, int count, int page = 0)
        {
            return _set.Where(c => c.CurrencyAmount > 0 && botId != c.UserId)
                .OrderByDescending(c => c.CurrencyAmount)
                .Skip(page * 9)
                .Take(count)
                .ToList();
        }

        public List<DiscordUser> GetTopRichest(ulong botId, int count)
        {
            return _set.Where(c => c.CurrencyAmount > 0 && botId != c.UserId)
                .OrderByDescending(c => c.CurrencyAmount)
                .Take(count)
                .ToList();
        }

        public long GetUserCurrency(ulong userId) =>
            _set.FirstOrDefault(x => x.UserId == userId)?.CurrencyAmount ?? 0;

        public void RemoveFromMany(List<ulong> ids)
        {
            var items = _set.Where(x => ids.Contains(x.UserId));
            foreach (var item in items)
            {
                item.CurrencyAmount = 0;
            }
        }

        public bool TryUpdateCurrencyState(ulong userId, string name, string discrim, string avatarId, long amount, bool allowNegative = false)
        {
            if (amount == 0)
                return true;

            // if remove - try to remove if he has more or equal than the amount
            // and return number of rows > 0 (was there a change)
            if (amount < 0 && !allowNegative)
            {
                if (_context.Database.IsNpgsql())
                {
                    var rows = _context.Database.ExecuteSqlCommand($@"
                                                                    UPDATE ""DiscordUser""
                                                                    SET ""CurrencyAmount""=""DiscordUser"".""CurrencyAmount""+{amount}
                                                                    WHERE ""UserId""={userId} AND ""CurrencyAmount"">={-amount};");
                    return rows > 0;
                }

                if (_context.Database.IsSqlServer())
                {
                    var rows = _context.Database.ExecuteSqlCommand($@"
                                                                    UPDATE DiscordUser
                                                                    SET CurrencyAmount=CurrencyAmount+{amount}
                                                                    WHERE UserId={userId} AND CurrencyAmount>={-amount};");
                    return rows > 0;
                }
            }

            // if remove and negative is allowed, just remove without any condition
            if (amount < 0 && allowNegative)
            {
                if (_context.Database.IsNpgsql())
                {
                    var rows = _context.Database.ExecuteSqlCommand($@"
                                                                    UPDATE ""DiscordUser""
                                                                    SET ""CurrencyAmount""=""DiscordUser"".""CurrencyAmount""+{amount}
                                                                    WHERE ""UserId""={userId};");
                    return rows > 0;
                }

                if (_context.Database.IsSqlServer())
                {
                    var rows = _context.Database.ExecuteSqlCommand($@"
                                                                    UPDATE DiscordUser
                                                                    SET CurrencyAmount=CurrencyAmount+{amount}
                                                                    WHERE UserId={userId};");
                    return rows > 0;
                }
            }

            // if add - create a new user with default values if it doesn't exist
            // if it exists, sum current amount with the new one, if it doesn't
            // he just has the new amount
            var updatedUserData = !string.IsNullOrWhiteSpace(name);
            name = name ?? "Unknown";
            discrim = discrim ?? "????";
            avatarId = avatarId ?? "";

            // just update the amount, there is no new user data
            if (!updatedUserData)
            {
                if (_context.Database.IsNpgsql())
                {
                    _context.Database.ExecuteSqlCommand($@"
                                                        INSERT INTO ""DiscordUser"" (""UserId"", ""Username"", ""Discriminator"", ""AvatarId"", ""CurrencyAmount"")
                                                        VALUES ({userId}, {name}, {discrim}, {avatarId}, {amount})
                                                        ON CONFLICT(""UserId"") DO UPDATE
                                                        SET ""CurrencyAmount""=""DiscordUser"".""CurrencyAmount""+{amount};");
                }

                if (_context.Database.IsSqlServer())
                {
                    _context.Database.ExecuteSqlCommand($@"
                                                        UPDATE DiscordUser
                                                        SET CurrencyAmount=CurrencyAmount+{amount}
                                                        WHERE UserId={userId}
                                                        IF @@ROWCOUNT=0
                                                            INSERT INTO DiscordUser (UserId, Username, Discriminator, AvatarId, CurrencyAmount)
                                                            VALUES ({userId}, {name}, {discrim}, {avatarId}, {amount});");
                }
            }
            else
            {
                if (_context.Database.IsNpgsql())
                {
                    _context.Database.ExecuteSqlCommand($@"
                                                        INSERT INTO ""DiscordUser"" (""UserId"", ""Username"", ""Discriminator"", ""AvatarId"", ""CurrencyAmount"")
                                                        VALUES ({userId}, {name}, {discrim}, {avatarId}, {amount})
                                                        ON CONFLICT(""UserId"") DO UPDATE
                                                        SET ""CurrencyAmount""=""DiscordUser"".""CurrencyAmount""+{amount},
                                                        ""Username""={name},
                                                        ""Discriminator""={discrim},
                                                        ""AvatarId""={avatarId};");
                }

                if (_context.Database.IsSqlServer())
                {
                    _context.Database.ExecuteSqlCommand($@"
                                                        UPDATE DiscordUser
                                                        SET CurrencyAmount=CurrencyAmount+{amount},
                                                        Username={name},
                                                        Discriminator={discrim},
                                                        AvatarId={avatarId}
                                                        WHERE UserId={userId}
                                                        IF @@ROWCOUNT=0
                                                            INSERT INTO DiscordUser (UserId, Username, Discriminator, AvatarId, CurrencyAmount)
                                                            VALUES ({userId}, {name}, {discrim}, {avatarId}, {amount});");
                }
            }
            return true;
        }

        public void CurrencyDecay(float decay, ulong botId)
        {
            if (_context.Database.IsNpgsql())
            {
                _context.Database.ExecuteSqlCommand($@"
                                                    UPDATE ""DiscordUser""
                                                    SET ""CurrencyAmount""=""DiscordUser"".""CurrencyAmount"" - ROUND(""DiscordUser"".""CurrencyAmount""*{decay}-0.5)
                                                    WHERE ""CurrencyAmount"" > 0 AND ""UserId""!={botId};");
            }

            if (_context.Database.IsSqlServer())
            {
                _context.Database.ExecuteSqlCommand($@"
                                                    UPDATE DiscordUser
                                                    SET CurrencyAmount=CurrencyAmount-ROUND(CurrencyAmount*{decay}-0.5)
                                                    WHERE CurrencyAmount>0 AND UserId!={botId};");
            }
        }

        public long GetCurrencyDecayAmount(float decay)
        {
            return (long)_set.Sum(x => Math.Round(x.CurrencyAmount * decay - 0.5));
        }

        public decimal GetTotalCurrency()
        {
            return _set
                .Sum(x => x.CurrencyAmount);
        }

        public decimal GetTopOnePercentCurrency(ulong botId)
        {
            return _set
                .Where(x => x.UserId != botId)
                .OrderByDescending(x => x.CurrencyAmount)
                .Take(_set.Count() / 100 == 0 ? 1 : _set.Count() / 100)
                .Sum(x => x.CurrencyAmount);
        }
    }
}
