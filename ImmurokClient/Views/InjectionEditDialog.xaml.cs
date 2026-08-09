using System.Windows;
using ImmurokClient.Localization;
using ImmurokClient.Services;
using Wpf.Ui.Controls;

namespace ImmurokClient.Views;

/// <summary>
/// 新增/编辑一条密码注入项：填名称、定位目标（准星捕获模板 + 应用身份）、设专属密码。
/// </summary>
public partial class InjectionEditDialog : FluentWindow
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    private readonly string _id;
    private bool _ok;

    // 捕获得到的目标状态（编辑时从既有项回填，或经准星重新捕获覆盖）
    private string _appId = "";
    private string _publisher = "";
    private string _signature = "";
    private string _packageFamilyName = "";
    private string _aumid = "";
    private string _originalFilename = "";
    private string _exeName = "";
    private string _fieldAutomationId = "";
    private string _fieldName = "";
    private string _fieldControlType = "";
    private string _fieldClassName = "";
    private string _windowTitle = "";
    private bool _fieldWasPassword;
    private bool _hasCapture;

    private InjectionEditDialog(PasswordInjectionItem? existing, bool isNew)
    {
        InitializeComponent();

        // isNew：从内置预设预填的新项——身份已经有了，但标题应当是「新增」而不是「编辑」。
        bool editing = existing is not null && !isNew;
        string title = T(editing ? "inject.dlg.edit" : "inject.dlg.add");
        Title = title;
        Titlebar.Title = title;

        if (existing is not null)
        {
            _id = existing.Id;
            NameBox.Text = existing.Name;
            _appId = existing.AppId;
            _publisher = existing.Publisher;
            _signature = existing.Signature;
            _packageFamilyName = existing.PackageFamilyName;
            _aumid = existing.Aumid;
            _originalFilename = existing.OriginalFilename;
            _exeName = existing.ExeName;
            _fieldAutomationId = existing.FieldAutomationId;
            _fieldName = existing.FieldName;
            _fieldControlType = existing.FieldControlType;
            _fieldClassName = existing.FieldClassName;
            _windowTitle = existing.WindowTitle;
            _fieldWasPassword = existing.FieldWasPassword;
            _hasCapture = existing.HasTarget;
            PasswordInput.Password = PasswordInjectionStore.GetPassword(existing) ?? "";
        }
        else
        {
            _id = new PasswordInjectionItem().Id; // 新建，生成一个 Id
        }

        UpdateCaptureUi();
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>
    /// 弹出对话框；返回保存后的项（取消返回 null）。existing 为 null 表示空白新增；
    /// isNew=true 表示 existing 只是预填模板（内置预设），标题按「新增」显示。
    /// </summary>
    public static PasswordInjectionItem? Show(
        Window? owner, PasswordInjectionItem? existing = null, bool isNew = false)
    {
        var dlg = new InjectionEditDialog(existing, isNew);
        if (owner is not null) dlg.Owner = owner;
        dlg.ShowDialog();
        return dlg._ok ? dlg._result : null;
    }

    private PasswordInjectionItem? _result;

    private void UpdateCaptureUi()
    {
        if (_hasCapture)
        {
            NoCaptureText.Visibility = Visibility.Collapsed;
            CaptureGrid.Visibility = Visibility.Visible;
            // 「应用」这一行显示的是**实际拿来匹配的那把钥匙**，不再一律显示 exe 路径：
            // 路径只在没有更强身份时才是匹配依据，照着路径显示会让人以为换个目录就失配是意外。
            AppMatchKind kind = Kind;
            MatchKindText.Text = T(kind switch
            {
                AppMatchKind.Package => "inject.match.package",
                AppMatchKind.Signature => "inject.match.signature",
                AppMatchKind.Path => "inject.match.path",
                _ => "common.none",
            });
            AppText.Text = kind switch
            {
                AppMatchKind.Package => _packageFamilyName,
                AppMatchKind.Signature => FileNameForDisplay(),
                AppMatchKind.Path => _appId,
                _ => T("common.none"),
            };
            PublisherText.Text = string.IsNullOrEmpty(_publisher) ? T("common.none") : _publisher;
            SignatureText.Text = string.IsNullOrEmpty(_signature) ? T("common.none") : Shorten(_signature);
            FieldIdText.Text = string.IsNullOrEmpty(_fieldAutomationId) ? T("common.none") : _fieldAutomationId;
            LocateBtn.Content = T("inject.capture.again");

            // 警告：捕获到的不是密码框 / 只能靠路径匹配（未签名且非打包）
            var warns = new System.Collections.Generic.List<string>();
            if (!_fieldWasPassword) warns.Add(T("inject.warn.notpassword"));
            if (kind == AppMatchKind.Path) warns.Add(T("inject.warn.pathonly"));
            WarnText.Text = string.Join("\n", warns);
            WarnText.Visibility = warns.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            NoCaptureText.Visibility = Visibility.Visible;
            CaptureGrid.Visibility = Visibility.Collapsed;
            WarnText.Visibility = Visibility.Collapsed;
            LocateBtn.Content = T("inject.capture");
        }
    }

    /// <summary>当前这份捕获里最强的那种身份（与 PasswordInjectionItem.BestIdentity 同口径）。</summary>
    private AppMatchKind Kind =>
        !string.IsNullOrEmpty(_packageFamilyName) ? AppMatchKind.Package
        : !string.IsNullOrEmpty(_publisher) ? AppMatchKind.Signature
        : !string.IsNullOrEmpty(_appId) ? AppMatchKind.Path
        : AppMatchKind.None;

    private string FileNameForDisplay()
    {
        if (!string.IsNullOrEmpty(_originalFilename)) return _originalFilename;
        if (!string.IsNullOrEmpty(_exeName)) return _exeName;
        try { return System.IO.Path.GetFileName(_appId); } catch { return _appId; }
    }

    private static string Shorten(string thumb) =>
        thumb.Length <= 16 ? thumb : thumb.Substring(0, 16) + "…";

    private void OnLocateClick(object sender, RoutedEventArgs e)
    {
        // 定位器（TargetPicker）会在取词期间隐藏整个 client（含本对话框），结束后自动还原，这里无需再移位。
        CapturedTarget? cap = TargetPicker.Pick(this);
        Activate();

        if (cap is null)
        {
            // 用户取消，或准星没落在有效控件上（含落在本窗口上）——给出可见提示，可再试。
            Error(T("inject.capture.failed"));
            return;
        }
        ErrText.Visibility = Visibility.Collapsed;

        _appId = cap.App.ExePath;
        _publisher = cap.App.Publisher;
        _signature = cap.App.Thumbprint;
        _packageFamilyName = cap.App.PackageFamilyName;
        _aumid = cap.App.Aumid;
        _originalFilename = cap.App.OriginalFilename;
        _exeName = cap.App.ExeName;
        _fieldAutomationId = cap.FieldAutomationId;
        _fieldName = cap.FieldName;
        _fieldControlType = cap.FieldControlType;
        _fieldClassName = cap.FieldClassName;
        _windowTitle = cap.WindowTitle;
        _fieldWasPassword = cap.IsPassword;
        _hasCapture = true; // 捕获到了元素即视为已定位（App 路径可能为空时下面会给出警告）

        // 名称为空时用宿主窗口标题或 exe 名兜底填一个
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = !string.IsNullOrEmpty(_windowTitle)
                ? _windowTitle
                : System.IO.Path.GetFileNameWithoutExtension(_appId);

        UpdateCaptureUi();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // 只强制要求名称——即便没定位目标也允许保存成卡片，之后可再「编辑」补定位。
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { Error(T("inject.need.name")); return; }

        var item = new PasswordInjectionItem
        {
            Id = _id,
            Name = NameBox.Text.Trim(),
            AppId = _appId,
            Publisher = _publisher,
            Signature = _signature,
            PackageFamilyName = _packageFamilyName,
            Aumid = _aumid,
            OriginalFilename = _originalFilename,
            ExeName = _exeName,
            FieldAutomationId = _fieldAutomationId,
            FieldName = _fieldName,
            FieldControlType = _fieldControlType,
            FieldClassName = _fieldClassName,
            WindowTitle = _windowTitle,
            FieldWasPassword = _fieldWasPassword,
        };
        PasswordInjectionStore.SetPassword(item, PasswordInput.Password);

        _result = item;
        _ok = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void Error(string msg)
    {
        ErrText.Text = msg;
        ErrText.Visibility = Visibility.Visible;
    }
}
