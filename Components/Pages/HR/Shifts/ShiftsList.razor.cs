using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Services;
using COCOBOLOERPNEW.Components.Pages.HR.Shifts.ShiftsComponents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using System.Text.Json;

namespace COCOBOLOERPNEW.Components.Pages.HR.Shifts;

public partial class ShiftsList : ComponentBase
{
    #region Injected Services
    
    [Inject] private IEmployeeShiftService ShiftService { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    
    #endregion

    #region Permissions
    
    private bool _hasViewPermission;
    private bool _hasAddPermission;
    private bool _hasEditPermission;
    private bool _hasDeletePermission;
    private bool _canSeeAllShifts;
    private bool _canOpenAttendance;
    private string _userName = "";
    
    #endregion

    #region Loading States
    
    private bool _isLoadingData = true;
    private bool _isLoadingStats = true;
    private bool _isExporting;
    private bool _isDeleting;
    
    #endregion

    #region Data
    
    private List<EmployeeShiftListDto> _shifts = new();
    private HashSet<EmployeeShiftListDto> _selectedShifts = new();
    private const string FilterStorageKey = "hr_shifts_list_filter_state";
    private EmployeeShiftFilterDto _filter = new();
    private bool _didInitialLoad;
    
    #endregion

    #region Pagination
    
    private int _currentPage = 1;
    private int _totalPages;
    private int _totalCount;
    private bool _hasPrevious;
    private bool _hasNext;
    
    #endregion

    #region Statistics
    
    private int _activeCount;
    private int _morningCount;
    private int _eveningCount;
    private int _dailyCount;
    
    #endregion

    #region Dialogs
    
    private bool _showImportDialog;
    private bool _showDeleteDialog;
    private EmployeeShiftListDto? _shiftToDelete;
    private ShiftFilters? _filtersComponent;
    
    #endregion

    #region UI Helpers

    private int SummaryActiveCount => _canSeeAllShifts ? _activeCount : _shifts.Count(s => s.IsActive);
    private int SummaryMorningCount => _canSeeAllShifts ? _morningCount : _shifts.Count(s => s.ShiftType == ShiftTypes.Morning);
    private int SummaryEveningCount => _canSeeAllShifts ? _eveningCount : _shifts.Count(s => s.ShiftType == ShiftTypes.Evening);
    private int SummaryDailyCount => _canSeeAllShifts ? _dailyCount : _shifts.Count(s => s.ShiftType == ShiftTypes.DailyWork);
    private int SummaryEndedCount => Math.Max(0, _totalCount - SummaryActiveCount);

    private int ActiveFiltersCount =>
        (string.IsNullOrWhiteSpace(_filter.SearchText) ? 0 : 1) +
        (string.IsNullOrWhiteSpace(_filter.ShiftType) ? 0 : 1) +
        (_filter.EffectiveFrom.HasValue ? 1 : 0) +
        (_filter.EffectiveTo.HasValue ? 1 : 0) +
        (_filter.ActiveOnly.HasValue ? 1 : 0);

    private int FromRecord => _canSeeAllShifts
        ? (_totalCount == 0 ? 0 : ((_currentPage - 1) * _filter.PageSize) + 1)
        : (_totalCount == 0 ? 0 : 1);

    private int ToRecord => _canSeeAllShifts
        ? Math.Min(_currentPage * _filter.PageSize, _totalCount)
        : _totalCount;

    private string CurrentScopeLabel => !_canSeeAllShifts
        ? "شيفتاتي"
        : _filter.ActiveOnly == true ? "الشيفتات النشطة"
        : _filter.ActiveOnly == false ? "الشيفتات المنتهية"
        : !string.IsNullOrWhiteSpace(_filter.ShiftType) ? $"نوع: {_filter.ShiftType}"
        : "كل الشيفتات";

    private string CurrentRangeLabel =>
        _filter.EffectiveFrom.HasValue && _filter.EffectiveTo.HasValue
            ? $"من {_filter.EffectiveFrom.Value:yyyy/MM/dd} إلى {_filter.EffectiveTo.Value:yyyy/MM/dd}"
            : _filter.EffectiveFrom.HasValue
                ? $"من {_filter.EffectiveFrom.Value:yyyy/MM/dd}"
                : _filter.EffectiveTo.HasValue
                    ? $"إلى {_filter.EffectiveTo.Value:yyyy/MM/dd}"
                    : "كل الفترات";

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        await LoadPermissions();

        if (!_hasViewPermission)
        {
            _isLoadingData = false;
            _isLoadingStats = false;
            return;
        }

        ApplyDefaultCurrentMonthRange();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || !_hasViewPermission || _didInitialLoad)
            return;

        _didInitialLoad = true;

        await RestoreFilterStateAsync();
        await LoadPageAsync();
    }

    private async Task LoadPermissions()
    {
        var authState = await AuthState.GetAuthenticationStateAsync();
        var user = authState.User;
        _userName = user.Identity?.Name ?? "System";

        // الأدوار المسموح لها برؤية جميع شيفتات الموظفين
        _canSeeAllShifts = user.IsInRole("Admin") || 
                           user.IsInRole("Hr") || 
                           user.IsInRole("HR") || 
                           user.IsInRole("HrManager") ||
                           user.IsInRole("HRManager") ||
                           user.IsInRole("Accountant") || 
                           user.IsInRole("Account") || 
                           user.IsInRole("SalesManager") || 
                           user.IsInRole("AccountManager") ||
                           user.HasClaim("Permission", "frm_AllShifts:View");

        _hasViewPermission = user.Identity?.IsAuthenticated == true;
        _canOpenAttendance = user.IsInRole("Admin")
                             || user.IsInRole("HrManager")
                             || user.IsInRole("HRManager")
                             || user.IsInRole("Accountant")
                             || user.IsInRole("AccountsManager")
                             || user.IsInRole("AccountManager")
                             || user.HasClaim("Permission", "frm_empAttendance:View")
                             || user.HasClaim("Permission", "frm_Attendance:View");
        
        _hasAddPermission = _canSeeAllShifts && (user.IsInRole("Admin") || user.IsInRole("HrManager") || user.IsInRole("HRManager") || user.HasClaim("Permission", "frm_EmpolyeeShifts:Add"));
        _hasEditPermission = _canSeeAllShifts && (user.IsInRole("Admin") || user.IsInRole("HrManager") || user.IsInRole("HRManager") || user.HasClaim("Permission", "frm_EmpolyeeShifts:Edit"));
        _hasDeletePermission = _canSeeAllShifts && (user.IsInRole("Admin") || user.IsInRole("HrManager") || user.IsInRole("HRManager") || user.HasClaim("Permission", "frm_EmpolyeeShifts:Delete"));
    }

    #endregion

    #region Data Loading

    private void ApplyDefaultCurrentMonthRange()
    {
        var today = DateTime.Today;
        _filter.EffectiveFrom = new DateTime(today.Year, today.Month, 1);
        _filter.EffectiveTo = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        _filter.PageSize = _filter.PageSize <= 0 ? 25 : _filter.PageSize;
        _filter.PageNumber = 1;
        _currentPage = 1;
    }

    private async Task LoadPageAsync()
    {
        _isLoadingData = true;
        _isLoadingStats = true;
        StateHasChanged();

        await LoadData();
        await LoadStatistics();

        _isLoadingData = false;
        _isLoadingStats = false;
        StateHasChanged();
    }

    private async Task RestoreFilterStateAsync()
    {
        if (!_canSeeAllShifts)
            return;

        try
        {
            var json = await JS.InvokeAsync<string?>("localStorage.getItem", FilterStorageKey);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var saved = JsonSerializer.Deserialize<ShiftFilterState>(json);
            if (saved is null)
                return;

            _filter.SearchText = saved.SearchText;
            _filter.ShiftType = saved.ShiftType;
            _filter.EffectiveFrom = saved.EffectiveFrom;
            _filter.EffectiveTo = saved.EffectiveTo;
            _filter.ActiveOnly = saved.ActiveOnly;
            _filter.PageSize = saved.PageSize > 0 ? saved.PageSize : 25;
            _filter.PageNumber = saved.PageNumber > 0 ? saved.PageNumber : 1;
            _currentPage = _filter.PageNumber;
        }
        catch
        {
            ApplyDefaultCurrentMonthRange();
        }
    }

    private async Task SaveFilterStateAsync()
    {
        if (!_canSeeAllShifts)
            return;

        try
        {
            var state = new ShiftFilterState
            {
                SearchText = _filter.SearchText,
                ShiftType = _filter.ShiftType,
                EffectiveFrom = _filter.EffectiveFrom,
                EffectiveTo = _filter.EffectiveTo,
                ActiveOnly = _filter.ActiveOnly,
                PageSize = _filter.PageSize,
                PageNumber = _currentPage
            };

            var json = JsonSerializer.Serialize(state);
            await JS.InvokeVoidAsync("localStorage.setItem", FilterStorageKey, json);
        }
        catch
        {
            // ignore browser storage issues
        }
    }

    private async Task LoadData()
    {
        if (_canSeeAllShifts)
        {
            _filter.PageNumber = _currentPage;
            var result = await ShiftService.GetShiftsAsync(_filter);
            
            _shifts = result.Items;
            _totalCount = result.TotalCount;
            _totalPages = result.TotalPages;
            _hasPrevious = result.HasPrevious;
            _hasNext = result.HasNext;
        }
        else
        {
            // الموظف العادي يرى شيفتاته الشخصية فقط
            _shifts = await ShiftService.GetMyShiftsAsync(_userName);
            _totalCount = _shifts.Count;
            _totalPages = 1;
            _hasPrevious = false;
            _hasNext = false;
        }
    }

    private async Task LoadStatistics()
    {
        if (!_canSeeAllShifts) return;

        try
        {
            var stats = await ShiftService.GetStatisticsAsync();
            _activeCount = stats.ActiveCount;
            _morningCount = stats.MorningCount;
            _eveningCount = stats.EveningCount;
            _dailyCount = stats.DailyCount;
        }
        catch
        {
            // Fallback
            _activeCount = _shifts.Count(s => s.IsActive);
            _morningCount = _shifts.Count(s => s.ShiftType == ShiftTypes.Morning);
            _eveningCount = _shifts.Count(s => s.ShiftType == ShiftTypes.Evening);
            _dailyCount = _shifts.Count(s => s.ShiftType == ShiftTypes.DailyWork);
        }
    }

    #endregion

    #region Filter Actions

     private async Task ApplyFilter()
    {
        _currentPage = 1;
        _filter.PageNumber = 1;
        _isLoadingData = true;
        StateHasChanged();

        await LoadData();
        await SaveFilterStateAsync();

        _isLoadingData = false;
        StateHasChanged();
    }

    private async Task ResetFilters()
    {
        var currentPageSize = _filter.PageSize > 0 ? _filter.PageSize : 25;
        _filter = new EmployeeShiftFilterDto { PageSize = currentPageSize };
        ApplyDefaultCurrentMonthRange();
        _filter.PageSize = currentPageSize;
        _currentPage = 1;
        _isLoadingData = true;
        StateHasChanged();

        await LoadData();
        await SaveFilterStateAsync();

        _isLoadingData = false;
        StateHasChanged();
    }

    private async Task RefreshView()
    {
        _isLoadingData = true;
        _isLoadingStats = true;
        StateHasChanged();

        await LoadData();
        await LoadStatistics();
        await SaveFilterStateAsync();

        _isLoadingData = false;
        _isLoadingStats = false;
        StateHasChanged();
    }

    #endregion

    #region Pagination

    private async Task OnPageChanged(int page)
    {
        _currentPage = page;
        _filter.PageNumber = page;
        _isLoadingData = true;
        StateHasChanged();

        await LoadData();
        await SaveFilterStateAsync();

        _isLoadingData = false;
        StateHasChanged();
    }

    private async Task OnPageSizeChanged(int pageSize)
    {
        _filter.PageSize = pageSize;
        _currentPage = 1;
        await ApplyFilter();
    }

    #endregion

    #region Selection

    private void OnSelectionChanged(HashSet<EmployeeShiftListDto> selected)
    {
        _selectedShifts = selected;
    }

    #endregion

    #region Navigation

    private void NavigateToAdd()
    {
        Nav.NavigateTo("/shifts/new");
    }

    private void NavigateToEdit(EmployeeShiftListDto shift)
    {
        Nav.NavigateTo($"/shifts/edit/{shift.EmployeeShiftId}");
    }

    #endregion

    #region Delete

    private void ConfirmDelete(EmployeeShiftListDto shift)
    {
        _shiftToDelete = shift;
        _showDeleteDialog = true;
    }

    private async Task DeleteShift()
    {
        if (_shiftToDelete == null) return;

        _isDeleting = true;
        StateHasChanged();
        
        var authState = await AuthState.GetAuthenticationStateAsync();
        var userName = authState.User.Identity?.Name ?? "System";

        var result = await ShiftService.DeleteShiftAsync(_shiftToDelete.EmployeeShiftId, userName);
        
        if (result.Success)
        {
            Snackbar.Add(result.Message, Severity.Success);
            _showDeleteDialog = false;
            
            await LoadData();
            await LoadStatistics();
            await SaveFilterStateAsync();
        }
        else
        {
            Snackbar.Add(result.Message, Severity.Error);
        }
        
        _isDeleting = false;
    }

    #endregion

    #region Export/Import

    private async Task ExportExcel()
    {
        _isExporting = true;
        StateHasChanged();
        
        try
        {
            var bytes = await ShiftService.ExportToExcelAsync(_filter);
            await DownloadFile(bytes, $"شيفتات_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            Snackbar.Add("تم التصدير بنجاح", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطأ في التصدير: {ex.Message}", Severity.Error);
        }
        
        _isExporting = false;
    }

    private async Task DownloadTemplate()
    {
        try
        {
            var bytes = await ShiftService.GetImportTemplateAsync();
            await DownloadFile(bytes, "shift_import_template.xlsx");
            Snackbar.Add("تم تحميل القالب", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطأ: {ex.Message}", Severity.Error);
        }
    }

    private void OpenImportDialog()
    {
        _showImportDialog = true;
    }

    private async Task OnImportCompleted(bool success)
    {
        if (success)
        {
            await LoadData();
            await LoadStatistics();
            await SaveFilterStateAsync();
        }
    }

    private async Task DownloadFile(byte[] bytes, string fileName)
    {
        var base64 = Convert.ToBase64String(bytes);
        await JS.InvokeVoidAsync("downloadFileFromBase64", base64, fileName);
    }

    #endregion

    private sealed class ShiftFilterState
    {
        public string? SearchText { get; set; }
        public string? ShiftType { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public bool? ActiveOnly { get; set; }
        public int PageSize { get; set; }
        public int PageNumber { get; set; }
    }
}
