using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Services;
using COCOBOLOERPNEW.Components.Pages.HR.Shifts.ShiftsComponents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;

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
    private EmployeeShiftFilterDto _filter = new();
    
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

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        await LoadPermissions();
        
        if (_hasViewPermission)
        {
            // ✅ تحميل بالتتابع - مش في نفس الوقت
            await LoadData();
            await LoadStatistics();
        }
        
        _isLoadingData = false;
        _isLoadingStats = false;
    }

    private async Task LoadPermissions()
    {
        var authState = await AuthState.GetAuthenticationStateAsync();
        var user = authState.User;

        _hasViewPermission = user.IsInRole("Admin") || user.HasClaim("Permission", "frm_AllShifts:View");
        _hasAddPermission = user.IsInRole("Admin") || user.HasClaim("Permission", "frm_EmpolyeeShifts:Add");
        _hasEditPermission = user.IsInRole("Admin") || user.HasClaim("Permission", "frm_EmpolyeeShifts:Edit");
        _hasDeletePermission = user.IsInRole("Admin") || user.HasClaim("Permission", "frm_EmpolyeeShifts:Delete");
    }

    #endregion

    #region Data Loading

    private async Task LoadData()
    {
        _filter.PageNumber = _currentPage;
        var result = await ShiftService.GetShiftsAsync(_filter);
        
        _shifts = result.Items;
        _totalCount = result.TotalCount;
        _totalPages = result.TotalPages;
        _hasPrevious = result.HasPrevious;
        _hasNext = result.HasNext;
    }

    private async Task LoadStatistics()
    {
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
            // Fallback - حساب من البيانات المحملة
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
        _isLoadingData = true;
        StateHasChanged();
        
        await LoadData();
        
        _isLoadingData = false;
        StateHasChanged();
    }

    private async Task ResetFilters()
    {
        _filter = new EmployeeShiftFilterDto();
        _currentPage = 1;
        _isLoadingData = true;
        StateHasChanged();
        
        await LoadData();
        
        _isLoadingData = false;
        StateHasChanged();
    }

    

    #endregion

    #region Pagination

    private async Task OnPageChanged(int page)
    {
        _currentPage = page;
        _isLoadingData = true;
        StateHasChanged();
        
        await LoadData();
        
        _isLoadingData = false;
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
            
            // ✅ تحميل بالتتابع
            await LoadData();
            await LoadStatistics();
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
            // ✅ تحميل بالتتابع
            await LoadData();
            await LoadStatistics();
        }
    }

    private async Task DownloadFile(byte[] bytes, string fileName)
    {
        var base64 = Convert.ToBase64String(bytes);
        await JS.InvokeVoidAsync("downloadFileFromBase64", base64, fileName);
    }

    #endregion
}