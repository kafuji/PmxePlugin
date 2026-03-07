using PEPlugin;
using PEPlugin.Pmd;
using PEPlugin.Pmx;
using PEPlugin.SDX;

namespace PhysicsChainAdjuster
{
	public class Plugin : IPEPlugin
	{
		private const string PluginName = "[Kafuji式] 物理チェーン自動調整";
		public string Name => PluginName;
		public string Version => "1.1.0";
		public string Description => "分岐を含むボーン木の剛体質量・ジョイントスプリングを補完設定します";

		// これがないとLimitedPluginLauncherがこのプラグインの名前を取得できない
		public IPEPluginOption Option { get; } =
			new PEPluginOption(false, true, PluginName);

		public void Run(IPERunArgs args)
		{
			try
			{
				var pmx = args.Host.Connector.Pmx.GetCurrentState();
				int[] selected = ResolveSelectedBoneIndices(args, pmx);

				if (selected.Length == 0)
				{
					return;
				}

				if (selected.Length == 1)
				{
					MessageBox.Show("2つ以上の関連剛体を持つボーンか、ジョイントで接続された剛体群を選択してください。", "エラー",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				if (!ValidateSelection(pmx, selected, out var validationError))
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
				MessageBox.Show("物理パラメータの設定が完了しました。", "完了",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"エラーが発生しました:\n{ex.Message}\n\n{ex.StackTrace}",
					"エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private int[] ResolveSelectedBoneIndices(IPERunArgs args, IPXPmx pmx)
		{
			int[] selectedBoneIndices = args.Host.Connector.View.PMDView.GetSelectedBoneIndices()
				?? Array.Empty<int>();
			if (selectedBoneIndices.Length > 0)
				return selectedBoneIndices;

			int[] selectedBodyIndices = args.Host.Connector.View.PMDView.GetSelectedBodyIndices()
				?? Array.Empty<int>();
			if (selectedBodyIndices.Length == 0)
				return Array.Empty<int>();

			var selectedBodyIndexSet = new HashSet<int>(selectedBodyIndices);
			return Enumerable.Range(0, pmx.Body.Count)
				.Where(i => selectedBodyIndexSet.Contains(i) && pmx.Body[i].Bone != null)
				.Select(i => pmx.Bone.IndexOf(pmx.Body[i].Bone))
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

		private bool ValidateSelection(IPXPmx pmx, int[] selectedIndices, out string error)
		{
			error = string.Empty;
			var selectedSet = new HashSet<IPXBone>(selectedIndices.Select(i => pmx.Bone[i]));

			if (!ValidateBoneHierarchy(selectedSet, out error))
			{
				return false;
			}

			var rigidMap = BuildRigidBodyMap(pmx);
			var selectedBodies = selectedSet
				.Where(rigidMap.ContainsKey)
				.SelectMany(b => rigidMap[b])
				.ToHashSet();

			if (selectedBodies.Any(body => body.Mode == BodyMode.DynamicWithBone))
			{
				error = "キネマティック剛体（ボーン追従剛体）が含まれています。対象から外してください。";
				return false;
			}

			if (HasJointCycle(pmx, selectedBodies))
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
				error = "選択ボーン列の先頭を判定できませんでした。";
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
						error = "選択ボーン内に循環参照が含まれています。";
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
				pair.Key.SpringConst_Rotate = new V3(spring, spring, spring);
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
		public bool UseLogInterp { get; set; } = true;
	}

	public class SettingDialog : Form
	{
		public PhysicsSettings Settings { get; private set; } = new PhysicsSettings();

		private NumericUpDown _massStart, _massEnd, _springStart, _springEnd;
		private RadioButton _rbLinear, _rbLog;
		private Label _chainInfoLabel;

		public SettingDialog(List<BoneComponent> components)
		{
			Text = "物理チェーン自動調整";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			StartPosition = FormStartPosition.CenterParent;
			Width = 360;
			Height = 340;
			MaximizeBox = false;
			MinimizeBox = false;

			int y = 12;

			_chainInfoLabel = new Label
			{
				Text = $"検出グループ数: {components.Count}件  " +
						 $"(各最大段数: {string.Join(", ", components.Select(c => c.MaxDepth + 1))})",
				Left = 12,
				Top = y,
				Width = 330,
				Height = 20
			};
			Controls.Add(_chainInfoLabel);
			y += 28;

			AddSectionLabel("剛体", y); y += 22;
			_massStart = AddRow("始点質量", 1.0f, ref y);
			_massEnd = AddRow("終端質量", 0.1f, ref y);
			y += 4;

			AddSectionLabel("スプリング（回転）", y); y += 22;
			_springStart = AddRow("始点", 100f, ref y);
			_springEnd = AddRow("終点", 10f, ref y);
			y += 4;

			AddSectionLabel("補完方式", y); y += 22;
			_rbLinear = new RadioButton { Text = "線形", Left = 20, Top = y, Width = 110 };
			_rbLog = new RadioButton { Text = "対数", Left = 140, Top = y, Width = 110, Checked = true };
			Controls.Add(_rbLinear);
			Controls.Add(_rbLog);
			y += 30;

			var btnOk = new Button
			{
				Text = "適用",
				DialogResult = DialogResult.OK,
				Left = 180,
				Top = y,
				Width = 72,
				Height = 28
			};
			var btnCancel = new Button
			{
				Text = "キャンセル",
				DialogResult = DialogResult.Cancel,
				Left = 260,
				Top = y,
				Width = 80,
				Height = 28
			};
			btnOk.Click += (_, __) =>
			{
				Settings = new PhysicsSettings
				{
					MassStart = (float)_massStart.Value,
					MassEnd = (float)_massEnd.Value,
					SpringStart = (float)_springStart.Value,
					SpringEnd = (float)_springEnd.Value,
					UseLogInterp = _rbLog.Checked
				};
			};
			Controls.Add(btnOk);
			Controls.Add(btnCancel);
			AcceptButton = btnOk;
			CancelButton = btnCancel;
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

		private NumericUpDown AddRow(string label, float defaultVal, ref int y)
		{
			Controls.Add(new Label { Text = label, Left = 20, Top = y + 2, Width = 150, Height = 18 });
			var nud = new NumericUpDown
			{
				Left = 175,
				Top = y,
				Width = 100,
				Height = 24,
				Minimum = 0,
				Maximum = 10000,
				DecimalPlaces = 2,
				Increment = 0.1m,
				Value = (decimal)defaultVal
			};
			Controls.Add(nud);
			y += 28;
			return nud;
		}
	}
}
