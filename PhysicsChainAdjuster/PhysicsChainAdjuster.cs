using PEPlugin;
using PEPlugin.Pmd;
using PEPlugin.Pmx;
using PEPlugin.SDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace PhysicsChainAdjuster
{
	public class Plugin : IPEPlugin
	{
		public string Name => "物理チェーン自動調整";
		public string Version => "1.0.0";
		public string Description => "ボーンチェーンの剛体質量・ジョイントスプリングを補完設定します";

		public IPEPluginOption Option { get; } =
			new PEPluginOption(false, true, "物理チェーン自動調整");

		public void Run(IPERunArgs args)
		{
			try
			{
				var pmx = args.Host.Connector.Pmx.GetCurrentState();
				int[] selected = ResolveSelectedBoneIndices(args, pmx);

				// PMX Editor起動時のプラグイン検証呼び出しでは未選択のため、静かに終了する
				if (selected.Length == 0)
				{
					return;
				}

				if (selected.Length == 1)
				{
					MessageBox.Show("2本以上のボーン、または関連ボーンを持つ剛体を選択してください。", "エラー",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				if (!ValidateSelectionIsIndependentChains(pmx, selected, out var validationError))
				{
					MessageBox.Show(
						$"{validationError}\n\nそれぞれに独立し、分岐のないボーン列を選択してください。",
						"エラー",
						MessageBoxButtons.OK,
						MessageBoxIcon.Warning);
					return;
				}

				// ---- ボーンチェーンの構築 ----
				var chains = BuildBoneChains(pmx, selected);

				if (chains.Count == 0)
				{
					MessageBox.Show("選択対象（ボーン/剛体）から有効なボーンチェーンを構築できませんでした。", "エラー",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				// ---- ダイアログ表示 ----
				using (var dlg = new SettingDialog(chains))
				{
					if (dlg.ShowDialog() != DialogResult.OK) return;

					ApplyParameters(pmx, chains, dlg.Settings);
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

		// 選択ボーンを優先し、未選択時は選択剛体から関連ボーンを解決する
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

		// ============================================================
		//  ボーンチェーン構築
		//  選択ボーンを「親→子」でつながるチェーンに分割する
		//  ※ 参照ベースで管理する
		// ============================================================
		private List<List<IPXBone>> BuildBoneChains(IPXPmx pmx, int[] selectedIndices)
		{
			var selectedSet = new HashSet<IPXBone>(selectedIndices.Select(i => pmx.Bone[i]));
			var chains = new List<List<IPXBone>>();

			// 選択ボーンのうち「親が選択に含まれない」ものを各チェーンの先頭とみなす
			var roots = selectedSet
				.Where(b => b.Parent == null || !selectedSet.Contains(b.Parent))
				.ToList();

			foreach (var root in roots)
			{
				var chain = new List<IPXBone>();
				var visited = new HashSet<IPXBone>();
				IPXBone? cur = root;
				while (cur != null && selectedSet.Contains(cur) && visited.Add(cur))
				{
					chain.Add(cur);
					// 子ボーンの中から選択済みのものを探す
					cur = pmx.Bone.FirstOrDefault(b => b.Parent == cur && selectedSet.Contains(b));
				}
				if (chain.Count >= 2) chains.Add(chain);
			}

			return chains;
		}

		// 選択ボーンが「独立した線形チェーンの集合」かどうかを検証する
		private bool ValidateSelectionIsIndependentChains(IPXPmx pmx, int[] selectedIndices, out string error)
		{
			error = string.Empty;
			var selectedSet = new HashSet<IPXBone>(selectedIndices.Select(i => pmx.Bone[i]));

			foreach (var bone in selectedSet)
			{
				int selectedChildCount = pmx.Bone.Count(b => b.Parent == bone && selectedSet.Contains(b));
				if (selectedChildCount > 1)
				{
					error = "選択ボーン内に分岐が含まれています。";
					return false;
				}
			}

			// 親参照を辿って、選択集合内に循環がないことを確認する
			var checkedNodes = new HashSet<IPXBone>();
			foreach (var start in selectedSet)
			{
				if (checkedNodes.Contains(start)) continue;

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

			// 2本以上選択されているため、少なくとも1本は選択内の親を持たない先頭が必要
			bool hasRoot = selectedSet.Any(b => b.Parent == null || !selectedSet.Contains(b.Parent));
			if (!hasRoot)
			{
				error = "選択ボーン列の先頭を判定できませんでした。";
				return false;
			}

			return true;
		}

		// ============================================================
		//  パラメータ適用
		// ============================================================
		private void ApplyParameters(IPXPmx pmx, List<List<IPXBone>> chains, PhysicsSettings s)
		{
			// ボーン参照 -> 紐づく剛体リストのマップ
			var rigidMap = BuildRigidBodyMap(pmx);

			foreach (var chain in chains)
			{
				int n = chain.Count; // ボーン数

				var jointSeq = BuildJointSequence(pmx, chain, rigidMap);

				// --- 剛体質量の設定 ---
				for (int k = 0; k < n; k++)
				{
					double t = (n == 1) ? 0.0 : (double)k / (n - 1);
					float mass = (float)Interpolate(s.MassStart, s.MassEnd, t, s.UseLogInterp);

					if (rigidMap.TryGetValue(chain[k], out var bodies))
					{
						foreach (var body in bodies)
							body.Mass = mass;
					}
				}

				// --- ジョイントスプリングの設定 ---
				// ジョイント数 = ボーン数 - 1 (各ボーン間に1つ)
				for (int k = 0; k < jointSeq.Count; k++)
				{
					int jTotal = jointSeq.Count;
					double t = (jTotal == 1) ? 0.0 : (double)k / (jTotal - 1);
					float spring = (float)Interpolate(s.SpringStart, s.SpringEnd, t, s.UseLogInterp);

					// 回転スプリングのX・Y・Zすべてに適用
					jointSeq[k].SpringConst_Rotate = new V3(spring, spring, spring);
				}
			}
		}

		// ボーン参照 -> 紐づく剛体参照リストのマップ
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

		// チェーン上のジョイントを順序付きで返す
		// 先頭ボーンの親が存在する場合は「親→先頭」も含め、以降はチェーン内の隣接ペアを抽出
		private List<IPXJoint> BuildJointSequence(IPXPmx pmx, List<IPXBone> chain,
			Dictionary<IPXBone, List<IPXBody>> rigidMap)
		{
			var result = new List<IPXJoint>();

			IPXJoint? FindJointBetween(IPXBone a, IPXBone b)
			{
				if (!rigidMap.TryGetValue(a, out var bodiesA)) return null;
				if (!rigidMap.TryGetValue(b, out var bodiesB)) return null;

				var setA = new HashSet<IPXBody>(bodiesA);
				var setB = new HashSet<IPXBody>(bodiesB);

				return pmx.Joint.FirstOrDefault(j =>
					(setA.Contains(j.BodyA) && setB.Contains(j.BodyB)) ||
					(setB.Contains(j.BodyA) && setA.Contains(j.BodyB)));
			}

			// 選択チェーンの先頭だけは、選択外の親とのジョイントも補完対象に含める
			var rootParent = chain[0].Parent;
			if (rootParent != null)
			{
				var parentJoint = FindJointBetween(rootParent, chain[0]);
				if (parentJoint != null)
					result.Add(parentJoint);
			}

			for (int seg = 0; seg < chain.Count - 1; seg++)
			{
				var joint = FindJointBetween(chain[seg], chain[seg + 1]);
				if (joint != null)
					result.Add(joint);
				// 1セグメントにつき最初に見つかった1つを使用
			}
			return result;
		}

		// 線形 / 対数補完
		private double Interpolate(double start, double end, double t, bool useLog)
		{
			if (!useLog)
				return start + (end - start) * t;

			// 対数補完: log空間で線形補完 (0値対策でepsilonを足す)
			const double eps = 1e-6;
			double logStart = Math.Log(Math.Max(start, eps));
			double logEnd = Math.Log(Math.Max(end, eps));
			return Math.Exp(logStart + (logEnd - logStart) * t);
		}

		public void Dispose()
		{
			// リソース解放が不要な場合は空実装
		}
	}

	// ============================================================
	//  設定値を保持するシンプルなDTO
	// ============================================================
	public class PhysicsSettings
	{
		public float MassStart { get; set; } = 1.0f;
		public float MassEnd { get; set; } = 0.1f;
		public float SpringStart { get; set; } = 100f;
		public float SpringEnd { get; set; } = 10f;
		public bool UseLogInterp { get; set; } = true;
	}

	// ============================================================
	//  設定ダイアログ
	// ============================================================
	public class SettingDialog : Form
	{
		public PhysicsSettings Settings { get; private set; } = new PhysicsSettings();

		private NumericUpDown _massStart, _massEnd, _springStart, _springEnd;
		private RadioButton _rbLinear, _rbLog;
		private Label _chainInfoLabel;

		public SettingDialog(List<List<IPXBone>> chains)
		{
			Text = "物理チェーン自動調整";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			StartPosition = FormStartPosition.CenterParent;
			Width = 360;
			Height = 340;
			MaximizeBox = false;
			MinimizeBox = false;

			int y = 12;

			// チェーン情報
			_chainInfoLabel = new Label
			{
				Text = $"検出チェーン数: {chains.Count}本  " +
						 $"(各チェーン長: {string.Join(", ", chains.Select(c => c.Count))} ボーン)",
				Left = 12,
				Top = y,
				Width = 330,
				Height = 20
			};
			Controls.Add(_chainInfoLabel);
			y += 28;

			// ---- 剛体質量 ----
			AddSectionLabel("剛体質量", y); y += 22;
			_massStart = AddRow("始点ボーン質量", 1.0f, ref y);
			_massEnd = AddRow("終端ボーン質量", 0.1f, ref y);
			y += 4;

			// ---- ジョイントスプリング ----
			AddSectionLabel("ジョイントスプリング（回転）", y); y += 22;
			_springStart = AddRow("始点スプリング", 100f, ref y);
			_springEnd = AddRow("終点スプリング", 10f, ref y);
			y += 4;

			// ---- 補完方式 ----
			AddSectionLabel("補完方式", y); y += 22;
			_rbLinear = new RadioButton { Text = "線形補完", Left = 20, Top = y, Width = 110 };
			_rbLog = new RadioButton { Text = "対数補完", Left = 140, Top = y, Width = 110, Checked = true };
			Controls.Add(_rbLinear);
			Controls.Add(_rbLog);
			y += 30;

			// ---- ボタン ----
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