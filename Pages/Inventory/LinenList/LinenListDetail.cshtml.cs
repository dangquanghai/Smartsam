using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinenList;

public class LinenListDetailModel : BasePageModel
{
    private const int FunctionId = 114;
    private const int PermissionView = 1;
    private const int PermissionUpdate = 2;

    private readonly PermissionService _permissionService;

    public LinenListDetailModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "view";

    [BindProperty]
    public PagePermissions PagePerm { get; set; } = new PagePermissions();

    [BindProperty]
    public LinenListInput Linen { get; set; } = new LinenListInput();

    public bool CanSave { get; set; }

    public IActionResult OnGet(int? id, string mode = "view")
    {
        PagePerm = GetUserPermissions();
        Mode = (mode ?? "view").ToLowerInvariant();

        if (Mode == "add")
        {
            if (!PagePerm.HasPermission(PermissionUpdate))
            {
                return RedirectToPage("./Index");
            }

            CanSave = true;
            Linen = new LinenListInput
            {
                IsLinen = true
            };
            return Page();
        }

        if (!id.HasValue || id.Value <= 0)
        {
            return RedirectToPage("./Index");
        }

        var row = LoadLinen(id.Value);
        if (row == null)
        {
            return NotFound();
        }

        Linen = row;
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            return RedirectToPage("./Index");
        }

        if (Mode == "edit" && !PagePerm.HasPermission(PermissionUpdate))
        {
            Mode = "view";
        }

        CanSave = Mode != "view" && PagePerm.HasPermission(PermissionUpdate);
        return Page();
    }

    public IActionResult OnPost()
    {
        PagePerm = GetUserPermissions();
        Mode = (Mode ?? "view").ToLowerInvariant();
        var isAdd = Mode == "add" || Linen.ID <= 0;

        if (!PagePerm.HasPermission(PermissionUpdate))
        {
            ModelState.AddModelError(string.Empty, "You do not have permission to update Linen List.");
            CanSave = false;
            return Page();
        }

        NormalizeInput();
        ValidateInput();
        if (!ModelState.IsValid)
        {
            CanSave = true;
            return Page();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        if (!isAdd && !LinenExists(conn, Linen.ID))
        {
            return NotFound();
        }

        using var cmd = conn.CreateCommand();
        if (isAdd)
        {
            cmd.CommandText = @"
INSERT INTO dbo.LN_Linnen
    (LinnenCode, IsLinen, IsUniform, VNDPriceNew3, Regular, IsOrder)
VALUES
    (@LinnenCode, @IsLinen, @IsUniform, @VNDPriceNew3, @Regular, @IsOrder);
SELECT CONVERT(int, SCOPE_IDENTITY());";
        }
        else
        {
            cmd.CommandText = @"
UPDATE dbo.LN_Linnen
SET LinnenCode = @LinnenCode,
    IsLinen = @IsLinen,
    IsUniform = @IsUniform,
    VNDPriceNew3 = @VNDPriceNew3,
    Regular = @Regular,
    IsOrder = @IsOrder
WHERE ID = @ID;";
            cmd.Parameters.Add("@ID", SqlDbType.Int).Value = Linen.ID;
        }

        BindSaveParameters(cmd);

        if (isAdd)
        {
            Linen.ID = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        else
        {
            cmd.ExecuteNonQuery();
        }

        TempData["SuccessMessage"] = isAdd ? "Linen item added." : "Linen item updated.";
        Mode = "edit";
        CanSave = true;
        ModelState.Clear();

        return RedirectToPage("./LinenListDetail", new { id = Linen.ID, mode = "edit" });
    }

    private LinenListInput? LoadLinen(int id)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT ID, LinnenCode, IsLinen, IsUniform, VNDPriceNew3, Regular, IsOrder
FROM dbo.LN_Linnen
WHERE ID = @ID;", conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = id;

        conn.Open();
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        return new LinenListInput
        {
            ID = Convert.ToInt32(rd["ID"]),
            LinnenCode = Convert.ToString(rd["LinnenCode"]) ?? string.Empty,
            IsLinen = ToBool(rd["IsLinen"]),
            IsUniform = ToBool(rd["IsUniform"]),
            EcoWashHcmc = Convert.ToString(rd["VNDPriceNew3"])?.Trim() ?? string.Empty,
            Regular = ToBool(rd["Regular"]),
            IsOrder = ToBool(rd["IsOrder"])
        };
    }

    private bool LinenExists(SqlConnection conn, int id)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.LN_Linnen WHERE ID = @ID;", conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = id;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private void NormalizeInput()
    {
        Linen.LinnenCode = (Linen.LinnenCode ?? string.Empty).Trim();
        Linen.EcoWashHcmc = (Linen.EcoWashHcmc ?? string.Empty).Trim().Replace(",", string.Empty);
    }

    private void ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Linen.LinnenCode))
        {
            ModelState.AddModelError("Linen.LinnenCode", "LinnenCode is required.");
        }

        if (Linen.LinnenCode.Length > 50)
        {
            ModelState.AddModelError("Linen.LinnenCode", "LinnenCode cannot exceed 50 characters.");
        }

        if (Linen.EcoWashHcmc.Length > 10)
        {
            ModelState.AddModelError("Linen.EcoWashHcmc", "Price (EcoWash HCMC) cannot exceed 10 characters.");
        }

        if (!string.IsNullOrWhiteSpace(Linen.EcoWashHcmc) && !Linen.EcoWashHcmc.All(char.IsDigit))
        {
            ModelState.AddModelError("Linen.EcoWashHcmc", "Price (EcoWash HCMC) must be numeric.");
        }
    }

    private void BindSaveParameters(SqlCommand cmd)
    {
        cmd.Parameters.Add("@LinnenCode", SqlDbType.VarChar, 50).Value = Linen.LinnenCode;
        cmd.Parameters.Add("@IsLinen", SqlDbType.Bit).Value = Linen.IsLinen;
        cmd.Parameters.Add("@IsUniform", SqlDbType.Bit).Value = Linen.IsUniform;
        cmd.Parameters.Add("@VNDPriceNew3", SqlDbType.Char, 10).Value =
            string.IsNullOrWhiteSpace(Linen.EcoWashHcmc) ? (object)DBNull.Value : Linen.EcoWashHcmc;
        cmd.Parameters.Add("@Regular", SqlDbType.Bit).Value = Linen.Regular;
        cmd.Parameters.Add("@IsOrder", SqlDbType.Int).Value = Linen.IsOrder ? 1 : 0;
    }

    private static bool ToBool(object value)
    {
        if (value == DBNull.Value)
        {
            return false;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        return Convert.ToInt32(value) != 0;
    }

    private PagePermissions GetUserPermissions()
    {
        var isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
        var roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
        var permsObj = new PagePermissions();

        if (isAdmin)
        {
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, FunctionId);
        }

        return permsObj;
    }
}

public class LinenListInput
{
    public int ID { get; set; }

    [Required]
    [StringLength(50)]
    public string LinnenCode { get; set; } = string.Empty;

    public bool IsLinen { get; set; }
    public bool IsUniform { get; set; }

    [StringLength(10)]
    public string EcoWashHcmc { get; set; } = string.Empty;

    public bool Regular { get; set; }
    public bool IsOrder { get; set; }
}
