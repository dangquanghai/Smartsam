using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinnenNoteDaily;

public class LinnenNoteDailyDetailModel : BasePageModel
{
    private const int FunctionId = 115;
    private const int PermissionView = 1;
    private const int PermissionUpdate = 2;
    private static readonly IReadOnlyDictionary<string, string> SupportedDetailColumnTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["BATH"] = "Bath",
        ["HAND"] = "Hand",
        ["FACE"] = "Face",
        ["BATH-MAT"] = "Bath mat",
        ["PILLOW-CASE"] = "Pillow case",
        ["SHEET-S"] = "Sheet S",
        ["D-COVER-S"] = "D Cover S",
        ["SHEET-K"] = "Sheet K",
        ["D-COVER-K"] = "D Cover K"
    };
    private static readonly IReadOnlyList<LinnenNoteDailyPantryDefinition> FixedPantries = new List<LinnenNoteDailyPantryDefinition>
    {
        new LinnenNoteDailyPantryDefinition(1, "03"),
        new LinnenNoteDailyPantryDefinition(2, "05"),
        new LinnenNoteDailyPantryDefinition(3, "06"),
        new LinnenNoteDailyPantryDefinition(4, "07"),
        new LinnenNoteDailyPantryDefinition(5, "08"),
        new LinnenNoteDailyPantryDefinition(6, "09"),
        new LinnenNoteDailyPantryDefinition(7, "11"),
        new LinnenNoteDailyPantryDefinition(8, "12"),
        new LinnenNoteDailyPantryDefinition(9, "14"),
        new LinnenNoteDailyPantryDefinition(10, "GOLF")
    };
    private readonly PermissionService _permissionService;
    private readonly IWebHostEnvironment _webHostEnvironment;

    public LinnenNoteDailyDetailModel(IConfiguration config, PermissionService permissionService, IWebHostEnvironment webHostEnvironment) : base(config)
    {
        _permissionService = permissionService;
        _webHostEnvironment = webHostEnvironment;
    }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "view";

    [BindProperty]
    public LinnenNoteDailyHeader Header { get; set; } = new LinnenNoteDailyHeader();

    [BindProperty]
    public string DetailsJson { get; set; } = "[]";

    [BindProperty]
    public bool ReturnToIndex { get; set; }

    [BindProperty]
    public string? RedirectUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Popup { get; set; }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public bool CanSave { get; private set; }
    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
    public bool IsPopup => Popup;
    public List<LinnenNoteDailyDetailRow> Details { get; set; } = new List<LinnenNoteDailyDetailRow>();
    public List<LinnenNoteDailyGridColumn> DetailColumns { get; private set; } = new List<LinnenNoteDailyGridColumn>();
    public IReadOnlyList<LinnenNoteDailyPantryDefinition> DetailPantries => FixedPantries;

    public IActionResult OnGet(int? id, string mode = "view", string? returnUrl = null)
    {
        PagePerm = GetUserPermissions();
        Mode = NormalizeMode(mode);
        ReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : string.Empty;

        if (Mode == "add")
        {
            if (!PagePerm.HasPermission(PermissionUpdate))
            {
                return RedirectToPage("./Index");
            }

            Header = new LinnenNoteDailyHeader
            {
                DateCreate = DateTime.Today,
                Description = GetSuggestedDescription()
            };

            using var conn = OpenConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                Header.Id = InsertHeader(conn, trans);
                ExecuteCreateDetails(conn, trans, Header.Id);
                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

            return RedirectToPage("./LinnenNoteDailyDetail", new { id = Header.Id, mode = "edit", returnUrl = ReturnUrl });
        }

        if (!id.HasValue || id.Value <= 0)
        {
            return RedirectToPage("./Index");
        }

        if (!LoadHeader(id.Value))
        {
            return NotFound();
        }

        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            return RedirectToPage("./Index");
        }

        if (Mode == "edit" && !PagePerm.HasPermission(PermissionUpdate))
        {
            Mode = "view";
        }

        if (Header.IsClose)
        {
            Mode = "view";
        }

        LoadDetailColumns();
        LoadDetails();
        DetailsJson = JsonSerializer.Serialize(Details);
        CanSave = Mode != "view" && PagePerm.HasPermission(PermissionUpdate) && !Header.IsClose;
        return Page();
    }

    public IActionResult OnGetPrintPreview(int id, string? linenCode = null)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            return new JsonResult(new { success = false, message = "Forbidden" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        if (id <= 0 || !LoadHeader(id))
        {
            return new JsonResult(new { success = false, message = "Pantry linen note not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        try
        {
            LoadDetailColumns();
            var rows = LoadPrintPreviewRows(id);
            var columns = GetPreviewColumns(linenCode);

            return new JsonResult(new
            {
                success = true,
                noteId = Header.Id,
                description = Header.Description,
                dateCreate = Header.DateCreate.ToString("yyyy-MM-dd"),
                columns,
                rows
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public IActionResult OnGetReportPdf(int id, bool download = false, string? linenCode = null)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            return RedirectToPage("./Index");
        }

        if (id <= 0 || !LoadHeader(id))
        {
            TempData["SuccessMessage"] = "Pantry linen note not found.";
            return RedirectToPage("./Index");
        }

        LoadDetailColumns();

        var report = new LinnenNoteDailyPdfReport
        {
            NoteId = Header.Id,
            Description = Header.Description,
            DateCreate = Header.DateCreate,
            CompanyLogo = LoadCompanyLogoBytes(),
            Columns = GetPreviewColumns(linenCode),
            Rows = LoadPrintPreviewRows(id)
        };

        var pdf = LinnenNoteDailyQuestPdfReport.BuildPdf(report);
        var fileName = $"PantryLinen_{Header.Id}.pdf";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
        return download
            ? File(pdf, "application/pdf", fileName)
            : File(pdf, "application/pdf");
    }

    public IActionResult OnPost()
    {
        PagePerm = GetUserPermissions();
        Mode = NormalizeMode(Mode);
        var isAdd = Header.Id <= 0 || Mode == "add";

        if (!PagePerm.HasPermission(PermissionUpdate))
        {
            ModelState.AddModelError(string.Empty, "You do not have permission to update pantry linen.");
        }

        NormalizeInput();
        ValidateInput();
        Details = ParseDetails();
        ValidateDetails();

        if (!ModelState.IsValid)
        {
            RedirectUrl = string.Empty;
            LoadDetailColumns();
            DetailsJson = JsonSerializer.Serialize(Details);
            CanSave = PagePerm.HasPermission(PermissionUpdate) && !Header.IsClose;
            return Page();
        }

        using var conn = OpenConnection();
        using var trans = conn.BeginTransaction();

        try
        {
            if (isAdd)
            {
                Header.Id = InsertHeader(conn, trans);
                ExecuteCreateDetails(conn, trans, Header.Id);
            }
            else
            {
                if (!HeaderExists(conn, trans, Header.Id))
                {
                    trans.Rollback();
                    return NotFound();
                }

                if (IsHeaderClosed(conn, trans, Header.Id))
                {
                    trans.Rollback();
                    ModelState.AddModelError(string.Empty, "This Pantry Linen note is closed and cannot be edited.");
                    Header.IsClose = true;
                    LoadDetailColumns();
                    DetailsJson = JsonSerializer.Serialize(Details);
                    CanSave = false;
                    Mode = "view";
                    return Page();
                }

                UpdateHeader(conn, trans);
                UpdateDetails(conn, trans, Details);
            }

            trans.Commit();
        }
        catch
        {
            trans.Rollback();
            throw;
        }

        TempData["SuccessMessage"] = isAdd ? "Pantry linen note added." : "Pantry linen note saved.";
        if (!string.IsNullOrWhiteSpace(RedirectUrl) && Url.IsLocalUrl(RedirectUrl))
        {
            return LocalRedirect(RedirectUrl);
        }

        if (ReturnToIndex)
        {
            return RedirectToPage("./Index");
        }

        return RedirectToPage("./LinnenNoteDailyDetail", new { id = Header.Id, mode = "edit", returnUrl = ReturnUrl });
    }

    private bool LoadHeader(int id)
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT ID, ISNULL(Des, '') AS Des, DateCreate, ISNULL(IsClose, 0) AS IsClose, ISNULL(Start, 0) AS Start, ISNULL(IsRent, 0) AS IsRent
FROM dbo.LN_DeAndReMT
WHERE ID = @ID;", conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = id;

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return false;
        }

        Header = new LinnenNoteDailyHeader
        {
            Id = Convert.ToInt32(rd["ID"]),
            Description = Convert.ToString(rd["Des"]) ?? string.Empty,
            DateCreate = rd["DateCreate"] == DBNull.Value ? DateTime.Today : Convert.ToDateTime(rd["DateCreate"]),
            IsClose = ToBool(rd["IsClose"]),
            Start = ToBool(rd["Start"]),
            IsRent = ToBool(rd["IsRent"])
        };
        return true;
    }

    private void LoadDetailColumns()
    {
        DetailColumns = new List<LinnenNoteDailyGridColumn>();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT LinnenCode,
       ISNULL(NULLIF(LinnenName, ''), LinnenCode) AS LinenName
FROM dbo.LN_Linnen
WHERE ISNULL(IsLinen, 0) = 1
  AND ISNULL(Regular, 0) = 1
ORDER BY ISNULL(IsOrder, 0), LinnenCode;", conn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var code = (Convert.ToString(rd["LinnenCode"]) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code) || !SupportedDetailColumnTitles.ContainsKey(code))
            {
                continue;
            }

            DetailColumns.Add(new LinnenNoteDailyGridColumn(code, SupportedDetailColumnTitles[code]));
        }
    }

    private void LoadDetails()
    {
        Details = new List<LinnenNoteDailyDetailRow>();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT dt.ID,
       dt.IDDeAndRe,
       dt.Pentry,
       ISNULL(p.PentryName, 'Pantry ' + CONVERT(varchar(10), dt.Pentry)) AS PentryName,
       dt.TimeSection,
       dt.LinenCode,
       ISNULL(NULLIF(l.LinnenName, ''), dt.LinenCode) AS LinenName,
       ISNULL(dt.Be, 0) AS Be,
       ISNULL(dt.De, 0) AS De,
       ISNULL(dt.Re, 0) AS Re
FROM dbo.LN_DeAndRe_DT dt
LEFT JOIN dbo.LN_Pentry p ON dt.Pentry = p.PentryID
INNER JOIN dbo.LN_Linnen l ON dt.LinenCode = l.LinnenCode
WHERE dt.IDDeAndRe = @IDDeAndRe
  AND ISNULL(l.IsLinen, 0) = 1
  AND ISNULL(l.Regular, 0) = 1
ORDER BY ISNULL(p.IsOrder, dt.Pentry), dt.Pentry, dt.TimeSection, ISNULL(l.IsOrder, 0), dt.LinenCode, dt.ID;", conn);
        cmd.Parameters.Add("@IDDeAndRe", SqlDbType.Int).Value = Header.Id;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            Details.Add(new LinnenNoteDailyDetailRow
            {
                Id = Convert.ToInt32(rd["ID"]),
                HeaderId = Convert.ToInt32(rd["IDDeAndRe"]),
                Pentry = Convert.ToInt32(rd["Pentry"]),
                PentryName = GetPantryDisplayName(Convert.ToInt32(rd["Pentry"]), Convert.ToString(rd["PentryName"])),
                TimeSection = Convert.ToInt32(rd["TimeSection"]),
                LinenCode = Convert.ToString(rd["LinenCode"]) ?? string.Empty,
                LinenName = Convert.ToString(rd["LinenName"]) ?? string.Empty,
                Be = ToInt(rd["Be"]),
                De = ToInt(rd["De"]),
                Re = ToInt(rd["Re"])
            });
        }
    }

    private int InsertHeader(SqlConnection conn, SqlTransaction trans)
    {
        var startValue = GetHeaderCount(conn, trans) <= 0;
        using var cmd = new SqlCommand(@"
INSERT INTO dbo.LN_DeAndReMT (DateCreate, IsClose, Start, Des)
VALUES (@DateCreate, 0, @Start, @Des);
SELECT CONVERT(int, SCOPE_IDENTITY());", conn, trans);
        BindHeaderParams(cmd, false);
        cmd.Parameters.Add("@Start", SqlDbType.Bit).Value = startValue;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private void UpdateHeader(SqlConnection conn, SqlTransaction trans)
    {
        using var cmd = new SqlCommand(@"
UPDATE dbo.LN_DeAndReMT
SET Des = @Des,
    DateCreate = @DateCreate,
    IsRent = @IsRent
WHERE ID = @ID;", conn, trans);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = Header.Id;
        BindHeaderParams(cmd, true);
        cmd.ExecuteNonQuery();
    }

    private void ExecuteCreateDetails(SqlConnection conn, SqlTransaction trans, int headerId)
    {
        using var cmd = new SqlCommand("dbo.Hai_CreateLinnenNoteDailyDT", conn, trans)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.Add("@NoteID", SqlDbType.Int).Value = headerId;
        cmd.ExecuteNonQuery();
    }

    private static void UpdateDetails(SqlConnection conn, SqlTransaction trans, IEnumerable<LinnenNoteDailyDetailRow> details)
    {
        foreach (var detail in details.Where(x => x.Id > 0))
        {
            using var cmd = new SqlCommand(@"
UPDATE dbo.LN_DeAndRe_DT
SET Be = @Be,
    De = @De,
    Re = @Re
WHERE ID = @ID
  AND IDDeAndRe = @IDDeAndRe
  AND Pentry = @Pentry
  AND TimeSection = @TimeSection
  AND LinenCode = @LinenCode;", conn, trans);
            cmd.Parameters.Add("@ID", SqlDbType.Int).Value = detail.Id;
            cmd.Parameters.Add("@IDDeAndRe", SqlDbType.Int).Value = detail.HeaderId;
            cmd.Parameters.Add("@Pentry", SqlDbType.Int).Value = detail.Pentry;
            cmd.Parameters.Add("@TimeSection", SqlDbType.Int).Value = detail.TimeSection;
            cmd.Parameters.Add("@LinenCode", SqlDbType.VarChar, 50).Value = detail.LinenCode;
            AddQuantityParameter(cmd, "@Be", detail.Be);
            AddQuantityParameter(cmd, "@De", detail.De);
            AddQuantityParameter(cmd, "@Re", detail.Re);
            cmd.ExecuteNonQuery();
        }
    }

    private static void AddQuantityParameter(SqlCommand cmd, string name, int value)
    {
        cmd.Parameters.Add(name, SqlDbType.Int).Value = value;
    }

    private void BindHeaderParams(SqlCommand cmd, bool includeRent)
    {
        cmd.Parameters.Add("@DateCreate", SqlDbType.DateTime).Value = Header.DateCreate.Date;
        cmd.Parameters.Add("@Des", SqlDbType.VarChar, 100).Value = Header.Description;
        if (includeRent)
        {
            cmd.Parameters.Add("@IsRent", SqlDbType.Bit).Value = Header.IsRent;
        }
    }

    private int GetHeaderCount(SqlConnection conn, SqlTransaction trans)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.LN_DeAndReMT;", conn, trans);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private bool HeaderExists(SqlConnection conn, SqlTransaction trans, int id)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.LN_DeAndReMT WHERE ID = @ID;", conn, trans);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = id;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool IsHeaderClosed(SqlConnection conn, SqlTransaction trans, int id)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(IsClose, 0) FROM dbo.LN_DeAndReMT WHERE ID = @ID;", conn, trans);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = id;
        return ToBool(cmd.ExecuteScalar() ?? false);
    }

    private string GetSuggestedDescription()
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("EXEC dbo.LN_GenNo 1, NULL;", conn);
        return Convert.ToString(cmd.ExecuteScalar())?.Trim() ?? string.Empty;
    }

    private List<LinnenNoteDailyDetailRow> ParseDetails()
    {
        if (string.IsNullOrWhiteSpace(DetailsJson))
        {
            return new List<LinnenNoteDailyDetailRow>();
        }

        return JsonSerializer.Deserialize<List<LinnenNoteDailyDetailRow>>(DetailsJson, new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            PropertyNameCaseInsensitive = true
        }) ?? new List<LinnenNoteDailyDetailRow>();
    }

    private void NormalizeInput()
    {
        Header.Description = (Header.Description ?? string.Empty).Trim();
        if (Header.DateCreate == default)
        {
            Header.DateCreate = DateTime.Today;
        }
    }

    private void ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Header.Description))
        {
            ModelState.AddModelError("Header.Description", "Note is required.");
        }
        if (Header.Description.Length > 100)
        {
            ModelState.AddModelError("Header.Description", "Note cannot exceed 100 characters.");
        }
    }

    private void ValidateDetails()
    {
        foreach (var detail in Details)
        {
            detail.LinenCode = (detail.LinenCode ?? string.Empty).Trim();

            if (detail.De < 0 || detail.Re < 0)
            {
                ModelState.AddModelError(string.Empty, "Delivery and receive quantities cannot be negative.");
                return;
            }
        }
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = (mode ?? "view").Trim().ToLowerInvariant();
        return normalized is "add" or "edit" or "view" ? normalized : "view";
    }

    private List<LinnenReportPreviewRow> LoadPrintPreviewRows(int noteId)
    {
        var rows = new List<LinnenReportPreviewRow>();
        var userCode = GetCurrentUserCode();

        using var conn = OpenConnection();
        using (var procCmd = new SqlCommand("dbo.LN_MakeLinenRPT", conn))
        {
            procCmd.CommandType = CommandType.StoredProcedure;
            procCmd.Parameters.Add("@PickupID", SqlDbType.Int).Value = noteId;
            procCmd.Parameters.Add("@UserCode", SqlDbType.VarChar, 15).Value = userCode;
            procCmd.ExecuteNonQuery();
        }

        using var cmd = new SqlCommand(@"
SELECT t.Pentry,
       ISNULL(p.PentryName, 'Pantry ' + CONVERT(varchar(10), t.Pentry)) AS PentryName,
       t.TimeSection,
       ISNULL(t.BathBe, 0) AS BathBe,
       ISNULL(t.BathDe, 0) AS BathDe,
       ISNULL(t.BathRe, 0) AS BathRe,
       ISNULL(t.HandBe, 0) AS HandBe,
       ISNULL(t.HandDe, 0) AS HandDe,
       ISNULL(t.HandRe, 0) AS HandRe,
       ISNULL(t.FaceBe, 0) AS FaceBe,
       ISNULL(t.FaceDe, 0) AS FaceDe,
       ISNULL(t.FaceRe, 0) AS FaceRe,
       ISNULL(t.BathMBe, 0) AS BathMBe,
       ISNULL(t.BathMDe, 0) AS BathMDe,
       ISNULL(t.BathMRe, 0) AS BathMRe,
       ISNULL(t.PillowBe, 0) AS PillowBe,
       ISNULL(t.PillowDe, 0) AS PillowDe,
       ISNULL(t.PillowRe, 0) AS PillowRe,
       ISNULL(t.SheetSBe, 0) AS SheetSBe,
       ISNULL(t.SheetSDe, 0) AS SheetSDe,
       ISNULL(t.SheetSRe, 0) AS SheetSRe,
       ISNULL(t.DCoverSBe, 0) AS DCoverSBe,
       ISNULL(t.DCoverSDe, 0) AS DCoverSDe,
       ISNULL(t.DCoverSRe, 0) AS DCoverSRe,
       ISNULL(t.SheetKBe, 0) AS SheetKBe,
       ISNULL(t.SheetKDe, 0) AS SheetKDe,
       ISNULL(t.SheetKRe, 0) AS SheetKRe,
       ISNULL(t.DCoverKBe, 0) AS DCoverKBe,
       ISNULL(t.DCoverKDe, 0) AS DCoverKDe,
       ISNULL(t.DCoverKRe, 0) AS DCoverKRe,
       ISNULL(t.KClothBe, 0) AS KClothBe,
       ISNULL(t.KClothDe, 0) AS KClothDe,
       ISNULL(t.KClothRe, 0) AS KClothRe,
       ISNULL(t.DClothBe, 0) AS DClothBe,
       ISNULL(t.DClothDe, 0) AS DClothDe,
       ISNULL(t.DClothRe, 0) AS DClothRe
FROM dbo.LN_PentryLinenTemp t
LEFT JOIN dbo.LN_Pentry p ON t.Pentry = p.PentryID
WHERE t.UserCode = @UserCode
  AND t.PickupID = @PickupID
ORDER BY ISNULL(p.IsOrder, t.Pentry), t.Pentry, t.TimeSection;", conn);
        cmd.Parameters.Add("@UserCode", SqlDbType.VarChar, 50).Value = userCode;
        cmd.Parameters.Add("@PickupID", SqlDbType.Int).Value = noteId;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new LinnenReportPreviewRow
            {
                Pentry = ToInt(rd["Pentry"]),
                PentryName = GetPantryDisplayName(ToInt(rd["Pentry"]), Convert.ToString(rd["PentryName"])),
                TimeSection = ToInt(rd["TimeSection"]),
                BathBe = ToInt(rd["BathBe"]),
                BathDe = ToInt(rd["BathDe"]),
                BathRe = ToInt(rd["BathRe"]),
                HandBe = ToInt(rd["HandBe"]),
                HandDe = ToInt(rd["HandDe"]),
                HandRe = ToInt(rd["HandRe"]),
                FaceBe = ToInt(rd["FaceBe"]),
                FaceDe = ToInt(rd["FaceDe"]),
                FaceRe = ToInt(rd["FaceRe"]),
                BathMBe = ToInt(rd["BathMBe"]),
                BathMDe = ToInt(rd["BathMDe"]),
                BathMRe = ToInt(rd["BathMRe"]),
                PillowBe = ToInt(rd["PillowBe"]),
                PillowDe = ToInt(rd["PillowDe"]),
                PillowRe = ToInt(rd["PillowRe"]),
                SheetSBe = ToInt(rd["SheetSBe"]),
                SheetSDe = ToInt(rd["SheetSDe"]),
                SheetSRe = ToInt(rd["SheetSRe"]),
                DCoverSBe = ToInt(rd["DCoverSBe"]),
                DCoverSDe = ToInt(rd["DCoverSDe"]),
                DCoverSRe = ToInt(rd["DCoverSRe"]),
                SheetKBe = ToInt(rd["SheetKBe"]),
                SheetKDe = ToInt(rd["SheetKDe"]),
                SheetKRe = ToInt(rd["SheetKRe"]),
                DCoverKBe = ToInt(rd["DCoverKBe"]),
                DCoverKDe = ToInt(rd["DCoverKDe"]),
                DCoverKRe = ToInt(rd["DCoverKRe"]),
                KClothBe = ToInt(rd["KClothBe"]),
                KClothDe = ToInt(rd["KClothDe"]),
                KClothRe = ToInt(rd["KClothRe"]),
                DClothBe = ToInt(rd["DClothBe"]),
                DClothDe = ToInt(rd["DClothDe"]),
                DClothRe = ToInt(rd["DClothRe"])
            });
        }

        return rows;
    }

    private List<LinnenReportPreviewColumn> GetPreviewColumns(string? linenCode)
    {
        var activeCodes = new HashSet<string>(DetailColumns.Select(x => x.Code), StringComparer.OrdinalIgnoreCase);
        var columns = new List<LinnenReportPreviewColumn>
        {
            new LinnenReportPreviewColumn("BATH", "Bath", "BathBe", "BathDe", "BathRe"),
            new LinnenReportPreviewColumn("HAND", "Hand", "HandBe", "HandDe", "HandRe"),
            new LinnenReportPreviewColumn("FACE", "Face", "FaceBe", "FaceDe", "FaceRe"),
            new LinnenReportPreviewColumn("BATH-MAT", "Bath mat", "BathMBe", "BathMDe", "BathMRe"),
            new LinnenReportPreviewColumn("PILLOW-CASE", "Pillow case", "PillowBe", "PillowDe", "PillowRe"),
            new LinnenReportPreviewColumn("SHEET-S", "Sheet S", "SheetSBe", "SheetSDe", "SheetSRe"),
            new LinnenReportPreviewColumn("D-COVER-S", "D Cover S", "DCoverSBe", "DCoverSDe", "DCoverSRe"),
            new LinnenReportPreviewColumn("SHEET-K", "Sheet K", "SheetKBe", "SheetKDe", "SheetKRe"),
            new LinnenReportPreviewColumn("D-COVER-K", "D Cover K", "DCoverKBe", "DCoverKDe", "DCoverKRe")
        }
        .Where(x => activeCodes.Contains(x.Code))
        .ToList();

        var code = (linenCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            return columns;
        }

        return columns.Where(x => x.Code == code).ToList();
    }

    private static string GetPantryDisplayName(int pentryId, string? fallbackName)
    {
        foreach (var pantry in FixedPantries)
        {
            if (pantry.PentryId == pentryId)
            {
                return pantry.DisplayName;
            }
        }

        return (fallbackName ?? string.Empty).Trim();
    }

    private string GetCurrentUserCode()
    {
        return User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "SYSTEM";
    }

    private byte[]? LoadCompanyLogoBytes()
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT TOP 1 ISNULL(CoLogo, '') AS CoLogo
FROM dbo.MS_Parameters;", conn);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var logoPath = ResolveCompanyLogoPathValue(Convert.ToString(reader["CoLogo"]));
        if (string.IsNullOrWhiteSpace(logoPath) || !System.IO.File.Exists(logoPath))
        {
            return null;
        }

        return System.IO.File.ReadAllBytes(logoPath);
    }

    private string ResolveCompanyLogoPathValue(string? logoFileName)
    {
        if (string.IsNullOrWhiteSpace(logoFileName))
        {
            return string.Empty;
        }

        var logoReference = logoFileName.Trim();
        if (System.IO.File.Exists(logoReference))
        {
            return logoReference;
        }

        var logoSegments = logoReference
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (logoSegments.Length == 0 || logoSegments.Any(x => x == "." || x == ".."))
        {
            return string.Empty;
        }

        var contentRootCandidate = Path.Combine([_webHostEnvironment.ContentRootPath, .. logoSegments]);
        if (System.IO.File.Exists(contentRootCandidate))
        {
            return contentRootCandidate;
        }

        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        var rootPath = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(_webHostEnvironment.ContentRootPath, basePath);

        var uploadRootCandidate = Path.Combine([rootPath, .. logoSegments]);
        if (System.IO.File.Exists(uploadRootCandidate))
        {
            return uploadRootCandidate;
        }

        return string.Empty;
    }

    private SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
    }

    private PagePermissions GetUserPermissions()
    {
        var isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
        var roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
        var perms = new PagePermissions();
        perms.AllowedNos = isAdmin
            ? Enumerable.Range(1, 20).ToList()
            : _permissionService.GetPermissionsForPage(roleId, FunctionId);
        return perms;
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

    private static int ToInt(object value)
    {
        return value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }
}

public class LinnenNoteDailyHeader
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Description { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime DateCreate { get; set; } = DateTime.Today;

    public bool IsClose { get; set; }
    public bool Start { get; set; }
    public bool IsRent { get; set; }
}

public class LinnenNoteDailyDetailRow
{
    public int Id { get; set; }
    public int HeaderId { get; set; }
    public int Pentry { get; set; }
    public string PentryName { get; set; } = string.Empty;
    public int TimeSection { get; set; }
    public string TimeSectionText => TimeSection == 1 ? "AM" : "PM";
    public string LinenCode { get; set; } = string.Empty;
    public string LinenName { get; set; } = string.Empty;
    public int Be { get; set; }
    public int De { get; set; }
    public int Re { get; set; }
}

public class LinnenNoteDailyGridColumn
{
    public LinnenNoteDailyGridColumn(string code, string title)
    {
        Code = code;
        Title = title;
    }

    public string Code { get; set; }
    public string Title { get; set; }
}

public class LinnenNoteDailyPantryDefinition
{
    public LinnenNoteDailyPantryDefinition(int pentryId, string displayName)
    {
        PentryId = pentryId;
        DisplayName = displayName;
    }

    public int PentryId { get; set; }
    public string DisplayName { get; set; }
}

public class LinnenReportPreviewRow
{
    public int Pentry { get; set; }
    public string PentryName { get; set; } = string.Empty;
    public int TimeSection { get; set; }
    public int BathBe { get; set; }
    public int BathDe { get; set; }
    public int BathRe { get; set; }
    public int HandBe { get; set; }
    public int HandDe { get; set; }
    public int HandRe { get; set; }
    public int FaceBe { get; set; }
    public int FaceDe { get; set; }
    public int FaceRe { get; set; }
    public int BathMBe { get; set; }
    public int BathMDe { get; set; }
    public int BathMRe { get; set; }
    public int PillowBe { get; set; }
    public int PillowDe { get; set; }
    public int PillowRe { get; set; }
    public int SheetSBe { get; set; }
    public int SheetSDe { get; set; }
    public int SheetSRe { get; set; }
    public int DCoverSBe { get; set; }
    public int DCoverSDe { get; set; }
    public int DCoverSRe { get; set; }
    public int SheetKBe { get; set; }
    public int SheetKDe { get; set; }
    public int SheetKRe { get; set; }
    public int DCoverKBe { get; set; }
    public int DCoverKDe { get; set; }
    public int DCoverKRe { get; set; }
    public int KClothBe { get; set; }
    public int KClothDe { get; set; }
    public int KClothRe { get; set; }
    public int DClothBe { get; set; }
    public int DClothDe { get; set; }
    public int DClothRe { get; set; }
}

public class LinnenReportPreviewColumn
{
    public LinnenReportPreviewColumn(string code, string title, string beField, string deField, string reField)
    {
        Code = code;
        Title = title;
        BeField = beField;
        DeField = deField;
        ReField = reField;
    }

    public string Code { get; set; }
    public string Title { get; set; }
    public string BeField { get; set; }
    public string DeField { get; set; }
    public string ReField { get; set; }
}
