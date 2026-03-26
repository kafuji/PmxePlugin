using PEPlugin;
using PEPlugin.Pmd;
using PEPlugin.Pmx;
using PEPlugin.SDX;
using System.Globalization;
using System.Text;
using System.Xml.Serialization;

namespace SetPhysParamsFromCSV;

// ── Plugin entry point ──────────────────────────────────────────────

public sealed class Plugin : IPEPlugin
{
	private const string PluginName = "[KAFUJI] CSV物理パラメータ適用";
	public string Name => PluginName;
	public string Version => "0.1.0";
	public string Description => "CSVファイルから剛体・ジョイントの物理パラメータを読み込み、名前が一致する対象に適用します";

	public IPEPluginOption Option { get; } =
		new PEPluginOption(false, true, PluginName);

	public void Run(IPERunArgs args)
	{
		try
		{
			var pmx = args.Host.Connector.Pmx.GetCurrentState();

			using var dlg = new MainDialog(pmx);
			if (dlg.ShowDialog() != DialogResult.OK)
				return;

			var (bodyCount, jointCount) = ApplyToModel(pmx, dlg.BodyRecords, dlg.JointRecords, dlg.Options);

			args.Host.Connector.Pmx.Update(pmx);
			args.Host.Connector.Form.UpdateList(UpdateObject.All);

			MessageBox.Show(
				$"適用完了\n剛体: {bodyCount}件\nジョイント: {jointCount}件",
				PluginName, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		catch (Exception ex)
		{
			MessageBox.Show($"エラーが発生しました:\n{ex.Message}\n\n{ex.StackTrace}",
				"エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private static (int Bodies, int Joints) ApplyToModel(
		IPXPmx pmx, List<BodyCsvRecord>? bodies, List<JointCsvRecord>? joints, ApplyOptions opts)
	{
		int bodyCount = 0;
		int jointCount = 0;

		if (bodies != null)
		{
			var bodyMap = new Dictionary<string, List<IPXBody>>();
			foreach (var b in pmx.Body)
			{
				if (!bodyMap.TryGetValue(b.Name, out var list))
				{
					list = new List<IPXBody>();
					bodyMap[b.Name] = list;
				}
				list.Add(b);
			}

			foreach (var rec in bodies)
			{
				if (!bodyMap.TryGetValue(rec.Name, out var targets))
					continue;

				foreach (var body in targets)
				{
					if (opts.BodyGroup)
						body.Group = rec.Group;
					if (opts.BodyPassGroup)
					{
						for (int i = 0; i < 16; i++)
							body.PassGroup[i] = rec.PassGroup[i];
					}
					if (opts.BodyMass)
						body.Mass = rec.Mass;
					if (opts.BodyDamping)
					{
						body.PositionDamping = rec.PositionDamping;
						body.RotationDamping = rec.RotationDamping;
					}
					if (opts.BodyRestitution)
						body.Restitution = rec.Restitution;
					if (opts.BodyFriction)
						body.Friction = rec.Friction;
					bodyCount++;
				}
			}
		}

		if (joints != null)
		{
			var jointMap = new Dictionary<string, List<IPXJoint>>();
			foreach (var j in pmx.Joint)
			{
				if (!jointMap.TryGetValue(j.Name, out var list))
				{
					list = new List<IPXJoint>();
					jointMap[j.Name] = list;
				}
				list.Add(j);
			}

			foreach (var rec in joints)
			{
				if (!jointMap.TryGetValue(rec.Name, out var targets))
					continue;

				foreach (var joint in targets)
				{
					if (opts.JointKind)
						joint.Kind = (JointKind)rec.Kind;
					if (opts.JointMoveLimit)
					{
						joint.Limit_MoveLow = rec.Limit_MoveLow;
						joint.Limit_MoveHigh = rec.Limit_MoveHigh;
					}
					if (opts.JointAngleLimit)
					{
						joint.Limit_AngleLow = rec.Limit_AngleLow;
						joint.Limit_AngleHigh = rec.Limit_AngleHigh;
					}
					if (opts.JointSpringMove)
						joint.SpringConst_Move = rec.SpringConst_Move;
					if (opts.JointSpringRotate)
						joint.SpringConst_Rotate = rec.SpringConst_Rotate;
					jointCount++;
				}
			}
		}

		return (bodyCount, jointCount);
	}

	public void Dispose() { }
}

// ── CSV data models ─────────────────────────────────────────────────

public class BodyCsvRecord
{
	public string Name { get; set; } = "";
	public int Group { get; set; }
	public bool[] PassGroup { get; set; } = new bool[16];
	public float Mass { get; set; }
	public float PositionDamping { get; set; }
	public float RotationDamping { get; set; }
	public float Restitution { get; set; }
	public float Friction { get; set; }

	public string PassGroupText =>
		string.Join(" ", Enumerable.Range(0, 16).Where(i => PassGroup[i]).Select(i => (i + 1).ToString()));
}

public class JointCsvRecord
{
	public string Name { get; set; } = "";
	public int Kind { get; set; }
	public V3 Limit_MoveLow { get; set; } = new V3(0, 0, 0);
	public V3 Limit_MoveHigh { get; set; } = new V3(0, 0, 0);
	public V3 Limit_AngleLow { get; set; } = new V3(0, 0, 0);
	public V3 Limit_AngleHigh { get; set; } = new V3(0, 0, 0);
	public V3 SpringConst_Move { get; set; } = new V3(0, 0, 0);
	public V3 SpringConst_Rotate { get; set; } = new V3(0, 0, 0);

	public static readonly string[] KindNames = { "ﾊﾞﾈ付6DOF", "6DOF", "P2P", "ConeTwist", "Slider", "Hinge" };
	public string KindText => Kind >= 0 && Kind < KindNames.Length ? KindNames[Kind] : Kind.ToString();
}

/// <summary>適用対象を制御するオプション。</summary>
public class ApplyOptions
{
	// 剛体
	public bool BodyGroup { get; set; } = true;
	public bool BodyPassGroup { get; set; } = true;
	public bool BodyMass { get; set; } = true;
	public bool BodyDamping { get; set; } = true;
	public bool BodyRestitution { get; set; } = true;
	public bool BodyFriction { get; set; } = true;
	// ジョイント
	public bool JointKind { get; set; } = true;
	public bool JointMoveLimit { get; set; } = true;
	public bool JointAngleLimit { get; set; } = true;
	public bool JointSpringMove { get; set; } = true;
	public bool JointSpringRotate { get; set; } = true;
}

// ── CSV parser (PMX Editor export format) ───────────────────────────
//
// PMX Editor の CSV 出力形式:
//   剛体: ;PmxBody,剛体名,剛体名(英),関連ボーン名,剛体タイプ,...,質量,移動減衰,回転減衰,反発力,摩擦力
//   Joint: ;PmxJoint,Joint名,Joint名(英),剛体名A,剛体名B,Jointタイプ,...
// ヘッダ行は ';' で始まるコメント行。データ行先頭は PmxBody / PmxJoint。

public static class CsvParser
{
	private const float Deg2Rad = (float)(Math.PI / 180.0);

	// ── Body columns (0-based) ──
	// 0:PmxBody 1:剛体名 2:剛体名(英) 3:関連ボーン名 4:剛体タイプ 5:グループ
	// 6:非衝突グループ 7:形状 8-10:サイズxyz 11-13:位置xyz 14-16:回転xyz[deg]
	// 17:質量 18:移動減衰 19:回転減衰 20:反発力 21:摩擦力
	private const int BodyCol_Name = 1;
	private const int BodyCol_Group = 5;
	private const int BodyCol_PassGroup = 6;
	private const int BodyCol_Mass = 17;
	private const int BodyCol_PosDamp = 18;
	private const int BodyCol_RotDamp = 19;
	private const int BodyCol_Restitution = 20;
	private const int BodyCol_Friction = 21;
	private const int BodyMinColumns = 22;

	// ── Joint columns (0-based) ──
	// 0:PmxJoint 1:Joint名 2:Joint名(英) 3:剛体名A 4:剛体名B 5:Jointタイプ
	// 6-8:位置xyz 9-11:回転xyz[deg]
	// 12-14:移動下限xyz 15-17:移動上限xyz
	// 18-20:回転下限xyz[deg] 21-23:回転上限xyz[deg]
	// 24-26:バネ定数-移動xyz 27-29:バネ定数-回転xyz
	private const int JointCol_Name = 1;
	private const int JointCol_Kind = 5;
	private const int JointCol_MoveLow = 12;
	private const int JointCol_MoveHigh = 15;
	private const int JointCol_AngleLow = 18;   // degrees in CSV
	private const int JointCol_AngleHigh = 21;  // degrees in CSV
	private const int JointCol_SpringMove = 24;
	private const int JointCol_SpringRotate = 27;
	private const int JointMinColumns = 30;

	public static List<BodyCsvRecord> ParseBodies(string filePath)
	{
		var lines = ReadCsvLines(filePath);
		var records = new List<BodyCsvRecord>();

		foreach (var line in lines)
		{
			if (IsCommentOrEmpty(line)) continue;
			var fields = SplitCsvLine(line);
			if (fields.Length < BodyMinColumns) continue;
			if (!fields[0].Equals("PmxBody", StringComparison.OrdinalIgnoreCase)) continue;

			var name = fields[BodyCol_Name];
			if (string.IsNullOrWhiteSpace(name)) continue;

			records.Add(new BodyCsvRecord
			{
				Name = name,
				Group = ParseInt(fields[BodyCol_Group]),
				PassGroup = ParsePassGroup(fields[BodyCol_PassGroup]),
				Mass = ParseFloat(fields[BodyCol_Mass]),
				PositionDamping = ParseFloat(fields[BodyCol_PosDamp]),
				RotationDamping = ParseFloat(fields[BodyCol_RotDamp]),
				Restitution = ParseFloat(fields[BodyCol_Restitution]),
				Friction = ParseFloat(fields[BodyCol_Friction]),
			});
		}

		if (records.Count == 0)
			throw new InvalidDataException("剛体CSVにデータ行がありません。PMX Editorで出力したCSVを指定してください。");
		return records;
	}

	public static List<JointCsvRecord> ParseJoints(string filePath)
	{
		var lines = ReadCsvLines(filePath);
		var records = new List<JointCsvRecord>();

		foreach (var line in lines)
		{
			if (IsCommentOrEmpty(line)) continue;
			var fields = SplitCsvLine(line);
			if (fields.Length < JointMinColumns) continue;
			if (!fields[0].Equals("PmxJoint", StringComparison.OrdinalIgnoreCase)) continue;

			var name = fields[JointCol_Name];
			if (string.IsNullOrWhiteSpace(name)) continue;

			records.Add(new JointCsvRecord
			{
				Name = name,
				Kind = ParseInt(fields[JointCol_Kind]),
				Limit_MoveLow = ParseV3(fields, JointCol_MoveLow),
				Limit_MoveHigh = ParseV3(fields, JointCol_MoveHigh),
				Limit_AngleLow = ParseV3Deg(fields, JointCol_AngleLow),
				Limit_AngleHigh = ParseV3Deg(fields, JointCol_AngleHigh),
				SpringConst_Move = ParseV3(fields, JointCol_SpringMove),
				SpringConst_Rotate = ParseV3(fields, JointCol_SpringRotate),
			});
		}

		if (records.Count == 0)
			throw new InvalidDataException("ジョイントCSVにデータ行がありません。PMX Editorで出力したCSVを指定してください。");
		return records;
	}

	/// <summary>ファイル先頭を読み、剛体CSVかジョイントCSVかを判定する。</summary>
	public static CsvFileType DetectType(string filePath)
	{
		try
		{
			var lines = ReadCsvLines(filePath);
			foreach (var line in lines)
			{
				if (IsCommentOrEmpty(line)) continue;
				var fields = SplitCsvLine(line);
				if (fields.Length == 0) continue;

				if (fields[0].Equals("PmxBody", StringComparison.OrdinalIgnoreCase))
					return CsvFileType.Body;
				if (fields[0].Equals("PmxJoint", StringComparison.OrdinalIgnoreCase))
					return CsvFileType.Joint;
				break; // first data line doesn't match
			}
		}
		catch { /* ignore detection errors */ }
		return CsvFileType.Unknown;
	}

	// ── helpers ──

	private static bool IsCommentOrEmpty(string line) =>
		string.IsNullOrWhiteSpace(line) || line[0] == ';';

	private static List<string> ReadCsvLines(string filePath)
	{
		// PMX Editor は BOM なし UTF-8 で CSV を出力する
		using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		var lines = new List<string>();
		string? line;
		while ((line = reader.ReadLine()) != null)
			lines.Add(line);
		return lines;
	}

	private static string[] SplitCsvLine(string line)
	{
		if (!line.Contains('"'))
			return line.Split(',').Select(f => f.Trim()).ToArray();

		var fields = new List<string>();
		int i = 0;
		while (i < line.Length)
		{
			if (line[i] == '"')
			{
				var sb = new StringBuilder();
				i++; // skip opening quote
				while (i < line.Length)
				{
					if (line[i] == '"')
					{
						if (i + 1 < line.Length && line[i + 1] == '"')
						{ sb.Append('"'); i += 2; }
						else
						{ i++; break; }
					}
					else { sb.Append(line[i]); i++; }
				}
				fields.Add(sb.ToString().Trim());
				if (i < line.Length && line[i] == ',') i++;
			}
			else
			{
				int start = i;
				while (i < line.Length && line[i] != ',') i++;
				fields.Add(line.Substring(start, i - start).Trim());
				if (i < line.Length) i++;
			}
		}
		return fields.ToArray();
	}

	private static float ParseFloat(string s) =>
		float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) ? val : 0f;

	private static int ParseInt(string s) =>
		int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) ? val : 0;

	/// <summary>スペース区切りの非衝突グループ文字列 (1-based: "1 2 3 4") → bool[16] (0-based)</summary>
	private static bool[] ParsePassGroup(string s)
	{
		var flags = new bool[16];
		if (string.IsNullOrWhiteSpace(s)) return flags;
		foreach (var token in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
		{
			if (int.TryParse(token, out int n) && n >= 1 && n <= 16)
				flags[n - 1] = true;
		}
		return flags;
	}

	private static V3 ParseV3(string[] fields, int startIndex) =>
		new V3(ParseFloat(fields[startIndex]), ParseFloat(fields[startIndex + 1]), ParseFloat(fields[startIndex + 2]));

	/// <summary>度数法で記載された3値を読み、ラジアンに変換して返す。</summary>
	private static V3 ParseV3Deg(string[] fields, int startIndex) =>
		new V3(
			ParseFloat(fields[startIndex]) * Deg2Rad,
			ParseFloat(fields[startIndex + 1]) * Deg2Rad,
			ParseFloat(fields[startIndex + 2]) * Deg2Rad);
}

public enum CsvFileType { Unknown, Body, Joint }

// ── Settings persistence ────────────────────────────────────────────

public class PluginSettings
{
	public string BodyCsvPath { get; set; } = "";
	public string JointCsvPath { get; set; } = "";
}

public static class PluginSettingsStore
{
	private static readonly XmlSerializer Serializer = new(typeof(PluginSettings));
	private static readonly string SettingsPath =
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SetPhysParamsFromCSV.settings.xml");

	public static PluginSettings Load()
	{
		try
		{
			if (!File.Exists(SettingsPath)) return new PluginSettings();
			using var stream = File.OpenRead(SettingsPath);
			return Serializer.Deserialize(stream) as PluginSettings ?? new PluginSettings();
		}
		catch { return new PluginSettings(); }
	}

	public static void Save(PluginSettings settings)
	{
		try
		{
			using var stream = File.Create(SettingsPath);
			Serializer.Serialize(stream, settings);
		}
		catch { }
	}
}

// ── Main dialog ─────────────────────────────────────────────────────

public class MainDialog : Form
{
	private readonly IPXPmx _pmx;
	private readonly HashSet<string> _modelBodyNames;
	private readonly HashSet<string> _modelJointNames;

	private TextBox _bodyPathBox = null!;
	private TextBox _jointPathBox = null!;
	private TabControl _tabControl = null!;
	private DataGridView _bodyGrid = null!;
	private DataGridView _jointGrid = null!;
	private Label _statusLabel = null!;
	private Font? _boldFont;

	// 適用オプション チェックボックス
	private CheckBox _chkBodyGroup = null!, _chkBodyPassGroup = null!;
	private CheckBox _chkBodyMass = null!, _chkBodyDamping = null!;
	private CheckBox _chkBodyRestitution = null!, _chkBodyFriction = null!;
	private CheckBox _chkJointKind = null!, _chkJointMoveLimit = null!;
	private CheckBox _chkJointAngleLimit = null!, _chkJointSpringMove = null!;
	private CheckBox _chkJointSpringRotate = null!;

	public List<BodyCsvRecord>? BodyRecords { get; private set; }
	public List<JointCsvRecord>? JointRecords { get; private set; }
	public ApplyOptions Options { get; private set; } = new();

	public MainDialog(IPXPmx pmx)
	{
		_pmx = pmx;
		_modelBodyNames = new HashSet<string>(pmx.Body.Select(b => b.Name));
		_modelJointNames = new HashSet<string>(pmx.Joint.Select(j => j.Name));

		InitializeComponents();

		var settings = PluginSettingsStore.Load();
		if (!string.IsNullOrEmpty(settings.BodyCsvPath))
		{
			_bodyPathBox.Text = settings.BodyCsvPath;
			LoadBodyCsv(settings.BodyCsvPath);
		}
		if (!string.IsNullOrEmpty(settings.JointCsvPath))
		{
			_jointPathBox.Text = settings.JointCsvPath;
			LoadJointCsv(settings.JointCsvPath);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) _boldFont?.Dispose();
		base.Dispose(disposing);
	}

	// ── UI construction ──

	private void InitializeComponents()
	{
		Text = "CSV物理パラメータ適用";
		Size = new Size(960, 640);
		MinimumSize = new Size(640, 480);
		StartPosition = FormStartPosition.CenterParent;
		AllowDrop = true;

		var mainLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(8),
			RowCount = 5,
			ColumnCount = 1,
		};
		mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // file paths
		mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // tab with grids
		mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // options
		mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // status
		mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // buttons

		// ── file path section ──
		var filePanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			RowCount = 2,
			ColumnCount = 3,
		};
		filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

		filePanel.Controls.Add(MakeLabel("剛体CSV:"), 0, 0);
		_bodyPathBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, AllowDrop = true };
		_bodyPathBox.DragEnter += CsvDragEnter;
		_bodyPathBox.DragDrop += (_, e) => HandleDrop(e, isBody: true);
		filePanel.Controls.Add(_bodyPathBox, 1, 0);
		var btnBrowseBody = new Button { Text = "参照...", AutoSize = true };
		btnBrowseBody.Click += (_, _) => BrowseCsv(isBody: true);
		filePanel.Controls.Add(btnBrowseBody, 2, 0);

		filePanel.Controls.Add(MakeLabel("ジョイントCSV:"), 0, 1);
		_jointPathBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, AllowDrop = true };
		_jointPathBox.DragEnter += CsvDragEnter;
		_jointPathBox.DragDrop += (_, e) => HandleDrop(e, isBody: false);
		filePanel.Controls.Add(_jointPathBox, 1, 1);
		var btnBrowseJoint = new Button { Text = "参照...", AutoSize = true };
		btnBrowseJoint.Click += (_, _) => BrowseCsv(isBody: false);
		filePanel.Controls.Add(btnBrowseJoint, 2, 1);

		mainLayout.Controls.Add(filePanel, 0, 0);

		// ── tab control with grids ──
		_tabControl = new TabControl { Dock = DockStyle.Fill };

		var bodyTab = new TabPage("剛体パラメータ");
		_bodyGrid = CreateBodyGrid();
		bodyTab.Controls.Add(_bodyGrid);
		_tabControl.TabPages.Add(bodyTab);

		var jointTab = new TabPage("ジョイントパラメータ");
		_jointGrid = CreateJointGrid();
		jointTab.Controls.Add(_jointGrid);
		_tabControl.TabPages.Add(jointTab);

		mainLayout.Controls.Add(_tabControl, 0, 1);

		// ── apply options checkboxes ──
		var optionsGroup = new GroupBox
		{
			Text = "適用項目",
			Dock = DockStyle.Fill,
			AutoSize = true,
			Padding = new Padding(6, 4, 6, 4),
		};
		var optFlow = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			WrapContents = true,
			FlowDirection = FlowDirection.LeftToRight,
		};
		_chkBodyGroup = MakeCheck("グループ", true);
		_chkBodyPassGroup = MakeCheck("非衝突G", true);
		_chkBodyMass = MakeCheck("質量", true);
		_chkBodyDamping = MakeCheck("減衰", true);
		_chkBodyRestitution = MakeCheck("反発力", true);
		_chkBodyFriction = MakeCheck("摩擦力", true);
		_chkJointKind = MakeCheck("Joint種類", true);
		_chkJointMoveLimit = MakeCheck("移動制限", true);
		_chkJointAngleLimit = MakeCheck("角度制限", true);
		_chkJointSpringMove = MakeCheck("バネ移動", true);
		_chkJointSpringRotate = MakeCheck("バネ回転", true);

		optFlow.Controls.AddRange(new Control[] {
			MakeSep("【剛体】"),
			_chkBodyGroup, _chkBodyPassGroup, _chkBodyMass, _chkBodyDamping,
			_chkBodyRestitution, _chkBodyFriction,
			MakeSep("  【Joint】"),
			_chkJointKind, _chkJointMoveLimit, _chkJointAngleLimit,
			_chkJointSpringMove, _chkJointSpringRotate,
		});
		optionsGroup.Controls.Add(optFlow);
		mainLayout.Controls.Add(optionsGroup, 0, 2);

		// ── status ──
		_statusLabel = new Label
		{
			Dock = DockStyle.Fill, AutoSize = true,
			Padding = new Padding(0, 4, 0, 4),
			Text = "CSVファイルを指定してください。"
		};
		mainLayout.Controls.Add(_statusLabel, 0, 3);

		// ── buttons ──
		var buttonPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.RightToLeft,
			AutoSize = true,
		};
		var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };
		var btnApply = new Button { Text = "適用", Width = 90, Height = 28 };
		btnApply.Click += OnApply;
		buttonPanel.Controls.Add(btnCancel);
		buttonPanel.Controls.Add(btnApply);
		mainLayout.Controls.Add(buttonPanel, 0, 4);

		AcceptButton = btnApply;
		CancelButton = btnCancel;
		Controls.Add(mainLayout);

		// form-level D&D
		DragEnter += CsvDragEnter;
		DragDrop += OnFormDragDrop;
	}

	private static Label MakeLabel(string text) =>
		new() { Text = text, Anchor = AnchorStyles.Left, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

	private static CheckBox MakeCheck(string text, bool isChecked) =>
		new() { Text = text, Checked = isChecked, AutoSize = true, Margin = new Padding(2, 2, 2, 2) };

	private static Label MakeSep(string text) =>
		new() { Text = text, AutoSize = true, Padding = new Padding(0, 4, 0, 0),
			Font = new Font(Control.DefaultFont, FontStyle.Bold) };

	// ── Grid setup ──

	private static DataGridView CreateBodyGrid()
	{
		var grid = new DataGridView
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			RowHeadersVisible = false,
		};
		grid.Columns.AddRange(
			new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "", FillWeight = 6 },
			new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名前", FillWeight = 26 },
			new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "G", FillWeight = 6 },
			new DataGridViewTextBoxColumn { Name = "PassGroup", HeaderText = "非衝突G", FillWeight = 16 },
			new DataGridViewTextBoxColumn { Name = "Mass", HeaderText = "質量", FillWeight = 10 },
			new DataGridViewTextBoxColumn { Name = "PosDamp", HeaderText = "移動減衰", FillWeight = 10 },
			new DataGridViewTextBoxColumn { Name = "RotDamp", HeaderText = "回転減衰", FillWeight = 10 },
			new DataGridViewTextBoxColumn { Name = "Restitution", HeaderText = "反発力", FillWeight = 10 },
			new DataGridViewTextBoxColumn { Name = "Friction", HeaderText = "摩擦力", FillWeight = 10 }
		);
		return grid;
	}

	private static DataGridView CreateJointGrid()
	{
		var grid = new DataGridView
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			RowHeadersVisible = false,
		};
		grid.Columns.AddRange(
			new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "", FillWeight = 5 },
			new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名前", FillWeight = 18 },
			new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "種類", FillWeight = 10 },
			new DataGridViewTextBoxColumn { Name = "MoveLow", HeaderText = "移動下限", FillWeight = 12 },
			new DataGridViewTextBoxColumn { Name = "MoveHigh", HeaderText = "移動上限", FillWeight = 12 },
			new DataGridViewTextBoxColumn { Name = "AngleLow", HeaderText = "角度下限", FillWeight = 12 },
			new DataGridViewTextBoxColumn { Name = "AngleHigh", HeaderText = "角度上限", FillWeight = 12 },
			new DataGridViewTextBoxColumn { Name = "SpringMove", HeaderText = "バネ移動", FillWeight = 12 },
			new DataGridViewTextBoxColumn { Name = "SpringRotate", HeaderText = "バネ回転", FillWeight = 12 }
		);
		return grid;
	}

	// ── Grid population ──

	private void PopulateBodyGrid()
	{
		_bodyGrid.Rows.Clear();
		if (BodyRecords == null) return;

		_boldFont ??= new Font(_bodyGrid.Font, FontStyle.Bold);

		foreach (var rec in BodyRecords)
		{
			bool found = _modelBodyNames.Contains(rec.Name);
			int idx = _bodyGrid.Rows.Add(
				found ? "○" : "✗",
				rec.Name,
				rec.Group.ToString(),
				rec.PassGroupText,
				rec.Mass.ToString("G"),
				rec.PositionDamping.ToString("G"),
				rec.RotationDamping.ToString("G"),
				rec.Restitution.ToString("G"),
				rec.Friction.ToString("G"));

			if (!found)
			{
				_bodyGrid.Rows[idx].DefaultCellStyle.ForeColor = Color.Red;
				_bodyGrid.Rows[idx].DefaultCellStyle.Font = _boldFont;
			}
		}
	}

	private void PopulateJointGrid()
	{
		_jointGrid.Rows.Clear();
		if (JointRecords == null) return;

		_boldFont ??= new Font(_jointGrid.Font, FontStyle.Bold);

		foreach (var rec in JointRecords)
		{
			bool found = _modelJointNames.Contains(rec.Name);
			int idx = _jointGrid.Rows.Add(
				found ? "○" : "✗",
				rec.Name,
				rec.KindText,
				FmtV3(rec.Limit_MoveLow),
				FmtV3(rec.Limit_MoveHigh),
				FmtV3Deg(rec.Limit_AngleLow),
				FmtV3Deg(rec.Limit_AngleHigh),
				FmtV3(rec.SpringConst_Move),
				FmtV3(rec.SpringConst_Rotate));

			if (!found)
			{
				_jointGrid.Rows[idx].DefaultCellStyle.ForeColor = Color.Red;
				_jointGrid.Rows[idx].DefaultCellStyle.Font = _boldFont;
			}
		}
	}

	private const float Rad2Deg = (float)(180.0 / Math.PI);
	private static string FmtV3(V3 v) => $"({v.X:G}, {v.Y:G}, {v.Z:G})";
	private static string FmtV3Deg(V3 v) => $"({v.X * Rad2Deg:F1}, {v.Y * Rad2Deg:F1}, {v.Z * Rad2Deg:F1})";

	private void UpdateStatus()
	{
		int bodyTotal = BodyRecords?.Count ?? 0;
		int bodyMatched = BodyRecords?.Count(r => _modelBodyNames.Contains(r.Name)) ?? 0;
		int jointTotal = JointRecords?.Count ?? 0;
		int jointMatched = JointRecords?.Count(r => _modelJointNames.Contains(r.Name)) ?? 0;

		var parts = new List<string>();
		if (bodyTotal > 0) parts.Add($"剛体: {bodyMatched}/{bodyTotal}件一致");
		if (jointTotal > 0) parts.Add($"ジョイント: {jointMatched}/{jointTotal}件一致");

		_statusLabel.Text = parts.Count > 0
			? string.Join("  |  ", parts)
			: "CSVファイルを指定してください。";
	}

	// ── CSV loading ──

	private void LoadBodyCsv(string path)
	{
		try
		{
			if (!File.Exists(path)) { BodyRecords = null; PopulateBodyGrid(); UpdateStatus(); return; }
			BodyRecords = CsvParser.ParseBodies(path);
			PopulateBodyGrid();
			UpdateStatus();
		}
		catch (Exception ex)
		{
			BodyRecords = null; PopulateBodyGrid(); UpdateStatus();
			MessageBox.Show($"剛体CSVの読み込みに失敗しました:\n{ex.Message}",
				"読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	private void LoadJointCsv(string path)
	{
		try
		{
			if (!File.Exists(path)) { JointRecords = null; PopulateJointGrid(); UpdateStatus(); return; }
			JointRecords = CsvParser.ParseJoints(path);
			PopulateJointGrid();
			UpdateStatus();
		}
		catch (Exception ex)
		{
			JointRecords = null; PopulateJointGrid(); UpdateStatus();
			MessageBox.Show($"ジョイントCSVの読み込みに失敗しました:\n{ex.Message}",
				"読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	// ── File browsing ──

	private void BrowseCsv(bool isBody)
	{
		using var ofd = new OpenFileDialog
		{
			Title = isBody ? "剛体CSVファイルを選択" : "ジョイントCSVファイルを選択",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
		};

		var currentPath = isBody ? _bodyPathBox.Text : _jointPathBox.Text;
		if (!string.IsNullOrEmpty(currentPath))
		{
			var dir = Path.GetDirectoryName(currentPath);
			if (dir != null && Directory.Exists(dir))
				ofd.InitialDirectory = dir;
		}

		if (ofd.ShowDialog() != DialogResult.OK) return;

		if (isBody)
		{ _bodyPathBox.Text = ofd.FileName; LoadBodyCsv(ofd.FileName); }
		else
		{ _jointPathBox.Text = ofd.FileName; LoadJointCsv(ofd.FileName); }
	}

	// ── Drag & Drop ──

	private static void CsvDragEnter(object? sender, DragEventArgs e)
	{
		e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
			? DragDropEffects.Copy
			: DragDropEffects.None;
	}

	private void HandleDrop(DragEventArgs e, bool isBody)
	{
		if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
		{
			if (isBody)
			{ _bodyPathBox.Text = files[0]; LoadBodyCsv(files[0]); }
			else
			{ _jointPathBox.Text = files[0]; LoadJointCsv(files[0]); }
		}
	}

	private void OnFormDragDrop(object? sender, DragEventArgs e)
	{
		if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
			return;

		if (files.Length >= 2)
		{
			// 2ファイル同時ドロップ: ヘッダから種別を自動判定
			var type0 = CsvParser.DetectType(files[0]);
			var type1 = CsvParser.DetectType(files[1]);
			string bodyFile, jointFile;
			if (type0 == CsvFileType.Joint && type1 != CsvFileType.Joint)
			{ bodyFile = files[1]; jointFile = files[0]; }
			else if (type1 == CsvFileType.Body && type0 != CsvFileType.Body)
			{ bodyFile = files[1]; jointFile = files[0]; }
			else
			{ bodyFile = files[0]; jointFile = files[1]; }

			_bodyPathBox.Text = bodyFile; LoadBodyCsv(bodyFile);
			_jointPathBox.Text = jointFile; LoadJointCsv(jointFile);
		}
		else
		{
			// 1ファイル: ヘッダから種別を自動判定
			var type = CsvParser.DetectType(files[0]);
			if (type == CsvFileType.Joint)
			{ _jointPathBox.Text = files[0]; LoadJointCsv(files[0]); }
			else
			{ _bodyPathBox.Text = files[0]; LoadBodyCsv(files[0]); }
		}
	}

	// ── Apply ──

	private void OnApply(object? sender, EventArgs e)
	{
		if (BodyRecords == null && JointRecords == null)
		{
			MessageBox.Show("CSVデータが読み込まれていません。",
				"確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		Options = new ApplyOptions
		{
			BodyGroup = _chkBodyGroup.Checked,
			BodyPassGroup = _chkBodyPassGroup.Checked,
			BodyMass = _chkBodyMass.Checked,
			BodyDamping = _chkBodyDamping.Checked,
			BodyRestitution = _chkBodyRestitution.Checked,
			BodyFriction = _chkBodyFriction.Checked,
			JointKind = _chkJointKind.Checked,
			JointMoveLimit = _chkJointMoveLimit.Checked,
			JointAngleLimit = _chkJointAngleLimit.Checked,
			JointSpringMove = _chkJointSpringMove.Checked,
			JointSpringRotate = _chkJointSpringRotate.Checked,
		};

		PluginSettingsStore.Save(new PluginSettings
		{
			BodyCsvPath = _bodyPathBox.Text,
			JointCsvPath = _jointPathBox.Text,
		});

		DialogResult = DialogResult.OK;
	}
}
