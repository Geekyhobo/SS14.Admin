using Content.Server.Database;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SS14.Admin.Helpers;

namespace SS14.Admin.Pages
{
    [Authorize(Roles = "PERMISSIONS")]
    [ValidateAntiForgeryToken]
    public class PermissonsModel : PageModel
    {
        private readonly PostgresServerDbContext _dbContext;

        public PermissonsModel(PostgresServerDbContext dbContext, BanHelper banHelper)
        {
            _dbContext = dbContext;
        }
        public List<AdminDisplayModel> Admins { get; set; } = new List<AdminDisplayModel>();

        public async Task OnGetAsync()
        {
            Admins = await _dbContext.Admin
                .Include(a => a.AdminRank)
                .ThenInclude(r => r.Flags)
                .Include(a => a.Flags)
                .Select(a => new
                {
                    Admin = a,
                    UserName = _dbContext.Player.Where(p => p.UserId == a.UserId).Select(p => p.LastSeenUserName).FirstOrDefault()
                })
                .Select(a => new AdminDisplayModel
                {
                    UserId = a.Admin.UserId,
                    UserName = a.UserName ?? "Unknown",
                    Title = a.Admin.Title ?? "N/A",
                    RankName = a.Admin.AdminRank != null ? a.Admin.AdminRank.Name : "No Rank",
                    Flags = a.Admin.Flags.Select(f => f.Flag).ToList()
                })
                .ToListAsync();
        }


        public class AdminDisplayModel
        {
            public string UserName { get; set; }
            public Guid UserId { get; set; }
            public string Title { get; set; }
            public string RankName { get; set; }
            public List<string> Flags { get; set; }
        }
    }
}
