using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartSam.Helpers;
namespace SmartSam.Pages.Shared
{
    [IgnoreAntiforgeryToken]
    public class LookupModel : PageModel
    {
        private readonly IConfiguration _config;
        public LookupModel(IConfiguration config)
        {
            _config = config;
        }
        public JsonResult OnGet(
            string table,
            string idField,
            string nameField,
            string term)
        {
            // NÊN whitelist để bảo mật
            var allowed = new[] { "CM_Company", "AM_Apmt" };
            if (!allowed.Contains(table))
                return new JsonResult(Array.Empty<object>());

            var data = Helper.LoadLookup(
                _config,
                table,
                idField,
                nameField,
                term
            );

            return new JsonResult(Helper.ToSelect2Result(data));
        }
    }
}
