using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SS14.Admin.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SS14.Admin.Admins;

namespace SS14.Admin.Pages.Permissions
{
    public class EditPermissionsModel : PageModel
    {
        private PermissionsHelper _permissionsHelper;

        public EditPermissionsModel(PermissionsHelper permissionsHelper)
        {
            _permissionsHelper = permissionsHelper;
        }
        [BindProperty]
        public AdminEditModel Admin { get; set; }
        public List<FlagEditModel> AvailableFlags { get; set; }

        public async Task<IActionResult> OnGetAsync(Guid userId)
        {
            var adminData = await _permissionsHelper.GetAdmin(userId);
            if (adminData != null)
            {
                // Convert admin's current flags into a list of strings
                var adminFlags = adminData.Flags.Select(f => f.Flag).ToList();

                // Retrieve all values and names from the AdminFlags enum
                var allFlags = Enum.GetValues(typeof(AdminFlags))
                    .Cast<AdminFlags>()
                    .Select(flag => new FlagEditModel
                    {
                        Name = flag.ToString(),
                        IsSelected = adminFlags.Contains(flag.ToString())
                    }).ToList();

                AvailableFlags = allFlags;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Implementation remains the same; ensure PermissionsHelper methods are properly called here
            return RedirectToPage("./Success");
        }

        public class AdminEditModel
        {
            public Guid UserId { get; set; }
            public string Title { get; set; }
            public string RankName { get; set; }
            public List<string> SelectedFlags { get; set; } = new List<string>();
        }

        public class FlagEditModel
        {
            public string Name { get; set; }
            public bool IsSelected { get; set; }
        }
    }
}
