using PEPlugin;
using PEPlugin.Pmd;
using PEPlugin.Pmx;
using PEPlugin.SDX;
using System.IO;
using System.Xml.Serialization;

namespace PhysicsChainAdjuster
{
	public class Plugin : IPEPlugin
	{
		private const string PluginName = "[KAFUJI] 物理チェーン自動調整";
		public string Name => PluginName;
		public string Version => "1.2.1";
		public string Description => "分岐を含むダイナミック剛体列（復数可）の質量・ジョイントスプリングを一括で補完設定します";

		// これがないとLimitedPluginLauncherがこのプラグインの名前を取得できない
		public IPEPluginOption Option { get; } =
			new PEPluginOption(false, true, PluginName);

		public void Run(IPERunArgs args)
		{
			try
			{
				var pmx = args.Host.Connector.Pmx.GetCurrentState();
				var selectedBodies = ResolveSelectedBodies(args, pmx);

				if (selectedBodies.Count == 0)
				{
					return;
				}

				if (selectedBodies.Any(body => body.Mode == BodyMode.Static))
				{
					MessageBox.Show("選択剛体にキネマティック剛体（ボーン追従剛体）が含まれています。対象から外してください。", "エラー",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				int[] selected = ResolveSelectedBoneIndicesFromBodies(pmx, selectedBodies);

				if (selected.Length == 0)
				{
					MessageBox.Show("選択剛体に対応するボーンを取得できませんでした。", "エラー",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				if (selected.Length == 1)
				{
					MessageBox.Show("ジョイントで接続された関連剛体を2つ以上選択してください。", "エラー",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				if (!ValidateSelection(pmx, selected, selectedBodies, out var validationError))
				{
					MessageBox.Show(
						validationError,
						"エラー",
						MessageBoxButtons.OK,
						MessageBoxIcon.Warning);
					return;
				}

				var components = BuildBoneComponents(pmx, selected);
				if (components.Count == 0)
				{
					MessageBox.Show("選択対象（ボーン/剛体）から有効なボーン木を構築できませんでした。", "エラー",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				using (var dlg = new SettingDialog(components))
				{
					if (dlg.ShowDialog() != DialogResult.OK) return;

					ApplyParameters(pmx, components, dlg.Settings);
				}

				args.Host.Connector.Pmx.Update(pmx);
				args.Host.Connector.Form.UpdateList(UpdateObject.All);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"エラーが発生しました:\n{ex.Message}\n\n{ex.StackTrace}",
					"エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private List<IPXBody> ResolveSelectedBodies(IPERunArgs args, IPXPmx pmx)
		{
			int[] selectedBodyIndices = args.Host.Connector.View.PMDView.GetSelectedBodyIndices()
				?? Array.Empty<int>();
			if (selectedBodyIndices.Length == 0)
				return new List<IPXBody>();

			return selectedBodyIndices
				.Where(i => i >= 0 && i < pmx.Body.Count)
				.Select(i => pmx.Body[i])
				.Distinct()
				.ToList();
		}

		private int[] ResolveSelectedBoneIndicesFromBodies(IPXPmx pmx, IEnumerable<IPXBody> selectedBodies)
		{
			return selectedBodies
				.Where(body => body.Bone != null)
				.Select(body => pmx.Bone.IndexOf(body.Bone))
				.Where(i => i >= 0)
				.Distinct()
				.ToArray();
		}

		private List<BoneComponent> BuildBoneComponents(IPXPmx pmx, int[] selectedIndices)
		{
			var selectedSet = new HashSet<IPXBone>(selectedIndices.Select(i => pmx.Bone[i]));
			var childMap = pmx.Bone
				.Where(selectedSet.Contains)
				.GroupBy(b => b.Parent)
				.ToDictionary(g => g.Key, g => g.ToList());

			var components = new List<BoneComponent>();
			var visited = new HashSet<IPXBone>();
			var roots = selectedSet
				.Where(b => b.Parent == null || !selectedSet.Contains(b.Parent))
				.OrderBy(b => pmx.Bone.IndexOf(b))
				.ToList();

			foreach (var root in roots)
			{
				if (visited.Contains(root))
				{
					continue;
				}

				var boneDepths = new Dictionary<IPXBone, int>();
				var stack = new Stack<(IPXBone Bone, int Depth)>();
				stack.Push((root, 0));

				while (stack.Count > 0)
				{
					var (bone, depth) = stack.Pop();
					if (!visited.Add(bone))
					{
						continue;
					}

					boneDepths[bone] = depth;
					if (!childMap.TryGetValue(bone, out var children))
					{
						continue;
					}

					for (int i = children.Count - 1; i >= 0; i--)
					{
						stack.Push((children[i], depth + 1));
					}
				}

				if (boneDepths.Count > 0)
				{
					components.Add(new BoneComponent(root, boneDepths));
				}
			}

			return components;
		}

		private bool ValidateSelection(IPXPmx pmx, int[] selectedIndices, IEnumerable<IPXBody> selectedBodies, out string error)
		{
			error = string.Empty;
			var selectedSet = new HashSet<IPXBone>(selectedIndices.Select(i => pmx.Bone[i]));

			if (!ValidateBoneHierarchy(selectedSet, out error))
			{
				return false;
			}

			var selectedBodySet = selectedBodies.ToHashSet();
			if (HasJointCycle(pmx, selectedBodySet))
			{
				error = "ジョイントの接続に循環が含まれています。循環しない木構造になるよう選択し直してください。";
				return false;
			}

			return true;
		}

		private bool ValidateBoneHierarchy(HashSet<IPXBone> selectedSet, out string error)
		{
			error = string.Empty;

			bool hasRoot = selectedSet.Any(b => b.Parent == null || !selectedSet.Contains(b.Parent));
			if (!hasRoot)
			{
				error = "選択剛体に対応するボーン列の先頭を判定できませんでした。";
				return false;
			}

			var checkedNodes = new HashSet<IPXBone>();
			foreach (var start in selectedSet)
			{
				if (checkedNodes.Contains(start))
				{
					continue;
				}

				var path = new HashSet<IPXBone>();
				IPXBone? cur = start;
				while (cur != null && selectedSet.Contains(cur))
				{
					if (!path.Add(cur))
					{
						error = "選択剛体に対応するボーン内に循環参照が含まれています。";
						return false;
					}

					checkedNodes.Add(cur);
					cur = cur.Parent;
				}
			}

			return true;
		}

		private bool HasJointCycle(IPXPmx pmx, HashSet<IPXBody> selectedBodies)
		{
			var adjacency = new Dictionary<IPXBody, List<(IPXBody Other, IPXJoint Joint)>>();

			foreach (var joint in pmx.Joint)
			{
				if (joint.BodyA == null || joint.BodyB == null)
				{
					continue;
				}

				if (!selectedBodies.Contains(joint.BodyA) || !selectedBodies.Contains(joint.BodyB))
				{
					continue;
				}

				if (!adjacency.TryGetValue(joint.BodyA, out var aList))
				{
					aList = new List<(IPXBody Other, IPXJoint Joint)>();
					adjacency[joint.BodyA] = aList;
				}
				aList.Add((joint.BodyB, joint));

				if (!adjacency.TryGetValue(joint.BodyB, out var bList))
				{
					bList = new List<(IPXBody Other, IPXJoint Joint)>();
					adjacency[joint.BodyB] = bList;
				}
				bList.Add((joint.BodyA, joint));
			}

			var visitedBodies = new HashSet<IPXBody>();
			var visitedJoints = new HashSet<IPXJoint>();

			foreach (var start in adjacency.Keys)
			{
				if (!visitedBodies.Add(start))
				{
					continue;
				}

				var stack = new Stack<(IPXBody Body, IPXBody? Parent)>();
				stack.Push((start, null));

				while (stack.Count > 0)
				{
					var (body, parent) = stack.Pop();
					foreach (var edge in adjacency[body])
					{
						if (!visitedJoints.Add(edge.Joint))
						{
							continue;
						}

						if (parent != null && ReferenceEquals(edge.Other, parent))
						{
							continue;
						}

						if (!visitedBodies.Add(edge.Other))
						{
							return true;
						}

						stack.Push((edge.Other, body));
					}
				}
			}

			return false;
		}

		private void ApplyParameters(IPXPmx pmx, List<BoneComponent> components, PhysicsSettings s)
		{
			var rigidMap = BuildRigidBodyMap(pmx);

			foreach (var component in components)
			{
				ApplyMasses(component, rigidMap, s);
				ApplyJointSprings(pmx, component, rigidMap, s);
			}
		}

		private void ApplyMasses(BoneComponent component, Dictionary<IPXBone, List<IPXBody>> rigidMap, PhysicsSettings s)
		{
			var bodiesWithDepth = component.BoneDepths
				.Where(pair => rigidMap.ContainsKey(pair.Key))
				.SelectMany(pair => rigidMap[pair.Key].Select(body => (Body: body, Depth: pair.Value)))
				.ToList();

			if (bodiesWithDepth.Count == 0)
			{
				return;
			}

			int maxDepth = bodiesWithDepth.Max(x => x.Depth);
			foreach (var item in bodiesWithDepth)
			{
				double t = maxDepth == 0 ? 0.0 : (double)item.Depth / maxDepth;
				float mass = (float)Interpolate(s.MassStart, s.MassEnd, t, s.UseLogInterp);
				item.Body.Mass = mass;
			}
		}

		private void ApplyJointSprings(IPXPmx pmx, BoneComponent component,
			Dictionary<IPXBone, List<IPXBody>> rigidMap, PhysicsSettings s)
		{
			var jointDepths = BuildJointDepthMap(pmx, component, rigidMap);
			if (jointDepths.Count == 0)
			{
				return;
			}

			int maxDepth = jointDepths.Values.Max();
			foreach (var pair in jointDepths)
			{
				double t = maxDepth == 0 ? 0.0 : (double)pair.Value / maxDepth;
				float spring = (float)Interpolate(s.SpringStart, s.SpringEnd, t, s.UseLogInterp);
				pair.Key.SpringConst_Rotate = new V3(
					spring * s.SpringAxisScaleX,
					spring * s.SpringAxisScaleY,
					spring * s.SpringAxisScaleZ);
			}
		}

		private Dictionary<IPXBone, List<IPXBody>> BuildRigidBodyMap(IPXPmx pmx)
		{
			var map = new Dictionary<IPXBone, List<IPXBody>>();
			foreach (var body in pmx.Body)
			{
				if (body.Bone == null) continue;
				if (!map.ContainsKey(body.Bone)) map[body.Bone] = new List<IPXBody>();
				map[body.Bone].Add(body);
			}
			return map;
		}

		private Dictionary<IPXJoint, int> BuildJointDepthMap(IPXPmx pmx, BoneComponent component,
			Dictionary<IPXBone, List<IPXBody>> rigidMap)
		{
			var result = new Dictionary<IPXJoint, int>();
			var componentBones = component.Bones.ToHashSet();

			foreach (var bone in component.Bones)
			{
				int depth = component.BoneDepths[bone];
				var parent = bone.Parent;
				if (parent == null)
				{
					continue;
				}

				bool isInternalEdge = componentBones.Contains(parent);
				bool isRootParentEdge = ReferenceEquals(bone, component.Root);
				if (!isInternalEdge && !isRootParentEdge)
				{
					continue;
				}

				foreach (var joint in FindJointsBetween(pmx, parent, bone, rigidMap))
				{
					if (!result.ContainsKey(joint))
					{
						result[joint] = depth;
					}
				}
			}

			return result;
		}

		private IEnumerable<IPXJoint> FindJointsBetween(IPXPmx pmx, IPXBone a, IPXBone b,
			Dictionary<IPXBone, List<IPXBody>> rigidMap)
		{
			if (!rigidMap.TryGetValue(a, out var bodiesA))
			{
				yield break;
			}

			if (!rigidMap.TryGetValue(b, out var bodiesB))
			{
				yield break;
			}

			var setA = new HashSet<IPXBody>(bodiesA);
			var setB = new HashSet<IPXBody>(bodiesB);

			foreach (var joint in pmx.Joint)
			{
				if (joint.BodyA == null || joint.BodyB == null)
				{
					continue;
				}

				bool ab = setA.Contains(joint.BodyA) && setB.Contains(joint.BodyB);
				bool ba = setB.Contains(joint.BodyA) && setA.Contains(joint.BodyB);
				if (ab || ba)
				{
					yield return joint;
				}
			}
		}

		private double Interpolate(double start, double end, double t, bool useLog)
		{
			if (!useLog)
				return start + (end - start) * t;

			const double eps = 1e-6;
			double logStart = Math.Log(Math.Max(start, eps));
			double logEnd = Math.Log(Math.Max(end, eps));
			return Math.Exp(logStart + (logEnd - logStart) * t);
		}

		public void Dispose()
		{
		}
	}

	public sealed class BoneComponent
	{
		public BoneComponent(IPXBone root, Dictionary<IPXBone, int> boneDepths)
		{
			Root = root;
			BoneDepths = boneDepths;
			Bones = boneDepths
				.OrderBy(pair => pair.Value)
				.ThenBy(pair => pair.Key.Name)
				.Select(pair => pair.Key)
				.ToList();
			MaxDepth = boneDepths.Count == 0 ? 0 : boneDepths.Values.Max();
		}

		public IPXBone Root { get; }
		public Dictionary<IPXBone, int> BoneDepths { get; }
		public List<IPXBone> Bones { get; }
		public int MaxDepth { get; }
	}

		public class PhysicsSettings
		{
			public float MassStart { get; set; } = 1.0f;
			public float MassEnd { get; set; } = 0.1f;
			public float SpringStart { get; set; } = 100f;
			public float SpringEnd { get; set; } = 10f;
			public float SpringAxisScaleX { get; set; } = 1.0f;
			public float SpringAxisScaleY { get; set; } = 1.0f;
			public float SpringAxisScaleZ { get; set; } = 1.0f;
			public bool UseLogInterp { get; set; } = true;
		}

	public class SettingDialog : Form
	{
		public PhysicsSettings Settings { get; private set; } = new PhysicsSettings();

		private TextBox _massStart = null!, _massEnd = null!, _springStart = null!, _springEnd = null!;
		private TextBox _springAxisScaleX = null!, _springAxisScaleY = null!, _springAxisScaleZ = null!;
		private RadioButton _rbLinear, _rbLog;
		private Label _selectionInfoLabel;
		private Label _depthInfoLabel;

		public SettingDialog(List<BoneComponent> components)
		{
			var initialSettings = PhysicsSettingsStore.Load();

			Text = "物理チェーン自動調整";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			StartPosition = FormStartPosition.CenterParent;
			Width = 320;
			Height = 388;
			MaximizeBox = false;
			MinimizeBox = false;

			int y = 12;

			_selectionInfoLabel = new Label
			{
				Text = $"選択剛体グループ数: {components.Count}件",
				Left = 12,
				Top = y,
				Width = 292,
				Height = 18
			};
			Controls.Add(_selectionInfoLabel);
			y += 20;

			_depthInfoLabel = new Label
			{
				Text = $"各最大段数: {string.Join(", ", components.Select(c => c.MaxDepth + 1))}",
				Left = 12,
				Top = y,
				Width = 292,
				Height = 28
			};
			Controls.Add(_depthInfoLabel);
			y += 34;

			AddSectionLabel("剛体質量", y); y += 20;
			_massStart = AddRow("始点", "1.0", ref y);
			_massEnd = AddRow("終点", "0.1", ref y);
			y += 2;

			AddSectionLabel("スプリング（回転）", y); y += 20;
			_springStart = AddRow("始点", "100", ref y);
			_springEnd = AddRow("終点", "10", ref y);
			y += 2;
			AddAxisScaleRow(ref y);
			y += 4;

			AddSectionLabel("補完方式", y); y += 20;
			_rbLog = new RadioButton { Text = "対数", Left = 20, Top = y, Width = 56, Checked = true };
			_rbLinear = new RadioButton { Text = "線形", Left = 88, Top = y, Width = 56 };
			Controls.Add(_rbLinear);
			Controls.Add(_rbLog);
			y += 26;

			var btnOk = new Button
			{
				Text = "適用",
				DialogResult = DialogResult.OK,
				Left = 150,
				Top = y,
				Width = 60,
				Height = 24
			};
			var btnCancel = new Button
			{
				Text = "キャンセル",
				DialogResult = DialogResult.Cancel,
				Left = 218,
				Top = y,
				Width = 72,
				Height = 24
			};
			btnOk.Click += (_, __) =>
			{
				if (!TryReadSettings(out var settings, out var errorMessage))
				{
					MessageBox.Show(errorMessage, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					DialogResult = DialogResult.None;
					return;
				}

				Settings = settings;
				PhysicsSettingsStore.Save(Settings);
			};
			Controls.Add(btnOk);
			Controls.Add(btnCancel);
			AcceptButton = btnOk;
			CancelButton = btnCancel;

			ApplySettingsToControls(initialSettings);
		}

		private void AddSectionLabel(string text, int y)
		{
			Controls.Add(new Label
			{
				Text = text,
				Left = 12,
				Top = y,
				Width = 200,
				Height = 18,
				Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold)
			});
		}

		private TextBox AddRow(string label, string defaultVal, ref int y)
		{
			Controls.Add(new Label { Text = label, Left = 20, Top = y + 3, Width = 64, Height = 18 });
			var textBox = new TextBox
			{
				Left = 108,
				Top = y,
				Width = 60,
				Height = 23,
				Text = defaultVal
			};
			Controls.Add(textBox);
			y += 24;
			return textBox;
		}

		private void AddAxisScaleRow(ref int y)
		{
			Controls.Add(new Label { Text = "軸ごとの係数", Left = 20, Top = y + 3, Width = 78, Height = 18 });
			Controls.Add(new Label { Text = "X軸", Left = 108, Top = y + 3, Width = 24, Height = 18 });
			_springAxisScaleX = new TextBox { Left = 132, Top = y, Width = 34, Height = 23, Text = "1.0" };
			Controls.Add(_springAxisScaleX);
			Controls.Add(new Label { Text = "Y軸", Left = 172, Top = y + 3, Width = 24, Height = 18 });
			_springAxisScaleY = new TextBox { Left = 196, Top = y, Width = 34, Height = 23, Text = "1.0" };
			Controls.Add(_springAxisScaleY);
			Controls.Add(new Label { Text = "Z軸", Left = 236, Top = y + 3, Width = 24, Height = 18 });
			_springAxisScaleZ = new TextBox { Left = 260, Top = y, Width = 34, Height = 23, Text = "1.0" };
			Controls.Add(_springAxisScaleZ);
			y += 24;
		}

		private void ApplySettingsToControls(PhysicsSettings settings)
		{
			_massStart.Text = settings.MassStart.ToString();
			_massEnd.Text = settings.MassEnd.ToString();
			_springStart.Text = settings.SpringStart.ToString();
			_springEnd.Text = settings.SpringEnd.ToString();
			_springAxisScaleX.Text = settings.SpringAxisScaleX.ToString();
			_springAxisScaleY.Text = settings.SpringAxisScaleY.ToString();
			_springAxisScaleZ.Text = settings.SpringAxisScaleZ.ToString();
			_rbLog.Checked = settings.UseLogInterp;
			_rbLinear.Checked = !settings.UseLogInterp;
		}

		private bool TryReadSettings(out PhysicsSettings settings, out string errorMessage)
		{
			settings = new PhysicsSettings();
			errorMessage = string.Empty;

			if (!TryParseNonNegativeFloat(_massStart.Text, out float massStart))
			{
				errorMessage = "剛体質量の始点に 0 以上の数値を入力してください。";
				return false;
			}

			if (!TryParseNonNegativeFloat(_massEnd.Text, out float massEnd))
			{
				errorMessage = "剛体質量の終点に 0 以上の数値を入力してください。";
				return false;
			}

			if (!TryParseNonNegativeFloat(_springStart.Text, out float springStart))
			{
				errorMessage = "スプリングの始点に 0 以上の数値を入力してください。";
				return false;
			}

			if (!TryParseNonNegativeFloat(_springEnd.Text, out float springEnd))
			{
				errorMessage = "スプリングの終点に 0 以上の数値を入力してください。";
				return false;
			}

			if (!TryParseNonNegativeFloat(_springAxisScaleX.Text, out float springAxisScaleX))
			{
				errorMessage = "X軸係数に 0 以上の数値を入力してください。";
				return false;
			}

			if (!TryParseNonNegativeFloat(_springAxisScaleY.Text, out float springAxisScaleY))
			{
				errorMessage = "Y軸係数に 0 以上の数値を入力してください。";
				return false;
			}

			if (!TryParseNonNegativeFloat(_springAxisScaleZ.Text, out float springAxisScaleZ))
			{
				errorMessage = "Z軸係数に 0 以上の数値を入力してください。";
				return false;
			}

			settings = new PhysicsSettings
			{
				MassStart = massStart,
				MassEnd = massEnd,
				SpringStart = springStart,
				SpringEnd = springEnd,
				SpringAxisScaleX = springAxisScaleX,
				SpringAxisScaleY = springAxisScaleY,
				SpringAxisScaleZ = springAxisScaleZ,
				UseLogInterp = _rbLog.Checked
			};
			return true;
		}

		private bool TryParseNonNegativeFloat(string text, out float value)
		{
			if (!float.TryParse(text, out value))
			{
				return false;
			}

			return value >= 0f;
		}
	}

	public static class PhysicsSettingsStore
	{
		private static readonly XmlSerializer Serializer = new(typeof(PhysicsSettings));
		private static readonly string SettingsPath =
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PhysicsChainAdjuster.settings.xml");

		public static PhysicsSettings Load()
		{
			try
			{
				if (!File.Exists(SettingsPath))
				{
					return new PhysicsSettings();
				}

				using var stream = File.OpenRead(SettingsPath);
				return Serializer.Deserialize(stream) as PhysicsSettings ?? new PhysicsSettings();
			}
			catch
			{
				return new PhysicsSettings();
			}
		}

		public static void Save(PhysicsSettings settings)
		{
			try
			{
				using var stream = File.Create(SettingsPath);
				Serializer.Serialize(stream, settings);
			}
			catch
			{
			}
		}
	}
}
