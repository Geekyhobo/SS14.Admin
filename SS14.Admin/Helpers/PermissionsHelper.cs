namespace SS14.Admin.Helpers;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

public class PermissionsHelper
{
    private readonly PostgresServerDbContext _dbContext;

    public PermissionsHelper(PostgresServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    //Used to Create A new admin
    public async Task CreateAdminAsync(Guid userId, string title, int? rankId, string[] flags)
    {
        var newAdmin = new Admin
        {
            UserId = userId,
            Title = title,
            AdminRankId = rankId,
            Flags = flags.Select(flag => new AdminFlag { Flag = flag }).ToList()
        };

        await _dbContext.Admin.AddAsync(newAdmin);
        await _dbContext.SaveChangesAsync();
    }

    //Modifys an exsiting Admins flags
    public async Task EditAdminPermissionsAsync(Guid userId, string[] flagsToAdd, string[] flagsToRemove)
    {
        var admin = await _dbContext.Admin
            .Include(a => a.Flags)
            .FirstOrDefaultAsync(a => a.UserId == userId);

        if (admin == null) throw new ArgumentException("Admin not found.");

        foreach (var flag in flagsToAdd.Except(admin.Flags.Select(f => f.Flag)))
        {
            admin.Flags.Add(new AdminFlag { Flag = flag, AdminId = userId });
        }

        foreach (var flag in flagsToRemove)
        {
            var flagToRemove = admin.Flags.FirstOrDefault(f => f.Flag == flag);
            if (flagToRemove != null)
            {
                admin.Flags.Remove(flagToRemove);
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<Admin?> GetAdmin(Guid guid)
    {
        var adminData = await _dbContext.Admin
            .Include(a => a.AdminRank)
            .ThenInclude(r => r!.Flags)
            .Include(a => a.Flags)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.UserId == guid);
        return adminData;
    }

    public async Task RemoveAdminAsync(Guid userId)
    {
        var admin = await _dbContext.Admin
            .FirstOrDefaultAsync(a => a.UserId == userId);

        if (admin != null)
        {
            _dbContext.Admin.Remove(admin);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task AddOrUpdateAdminRankAsync(int rankId, string rankName, string[] flags)
    {
        var rank = await _dbContext.AdminRank
            .Include(r => r.Flags)
            .FirstOrDefaultAsync(r => r.Id == rankId) ?? new AdminRank { Id = rankId };

        rank.Name = rankName;

        // Update flags
        rank.Flags.Clear();
        foreach (var flag in flags)
        {
            rank.Flags.Add(new AdminRankFlag { Flag = flag, AdminRankId = rank.Id });
        }

        if (rank.Id == 0)
        {
            _dbContext.AdminRank.Add(rank);
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task RemoveAdminRankAsync(int rankId)
    {
        var rank = await _dbContext.AdminRank.FindAsync(rankId);
        if (rank != null)
        {
            _dbContext.AdminRank.Remove(rank);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task EditAdminTitleAsync(Guid userId, string newTitle)
    {
        var admin = await _dbContext.Admin.FindAsync(userId);
        if (admin != null)
        {
            admin.Title = newTitle;
            await _dbContext.SaveChangesAsync();
        }
    }
}
