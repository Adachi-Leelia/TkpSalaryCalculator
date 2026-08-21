# コードレビュー結果

## 1. レビュー情報

* **レビュー対象**: ホームと給与期間サマリー（`SCR-HOME-01`）の初期実装
* **レビュー日時**: 2026-08-22
* **レビュー回数**: 1
* **レビュー対象ブランチ / コミット**: `develop` / `1b772b3`（比較元: `419fdcd`、確認時HEAD: `1b772b3`）
* **参照した仕様**: `docs/ui_implementation_plan.md` 123～133行、`docs/requirements.md`、`docs/screen_specification.md`、`docs/test_specification.md`、`docs/adr/0001-ui-framework.md`、Microsoft Learn「Build accessible apps with semantic properties - .NET MAUI」（https://learn.microsoft.com/dotnet/maui/fundamentals/accessibility?view=net-maui-10.0）
* **レビュー範囲**: `1b772b3`で追加・変更されたホームPage、期間ヘッダー、バックアップ案内、画面遷移、DI／ルート登録、App層テスト、および直接利用する共通ViewModelとApplication層契約
* **レビュー対象外**: 後続段階で実装予定のカレンダー、計算内訳、月額手当、未計算日一覧の画面内容、指摘事項の修正、Android実機での見た目・TalkBack・最大文字サイズ確認。AndroidアプリビルドはJDK未設定（`XA5300`）のため未完了

## 2. 総合結果

**判定**: `NEEDS_FIX`

### サマリー

* Critical: 0件
* High: 0件
* Medium: 2件
* Low: 1件

### 総評

給与算定開始日・終了日、合計と4区分の小計、期間移動、未計算件数、現在日のカレンダー遷移、バックアップ案内と7日延期は責務を分けて実装され、App層テスト70件も成功した。一方、主要な日付と合計金額は`Label`へ固定のセマンティック説明を設定したためスクリーンリーダーで値が読み上げられず、画面離脱直後の再表示では最新状態の再読込が失われる競合がある。また、未計算件数が0件の期間では仕様上の表示項目そのものが非表示になる。主要情報のアクセシビリティと再読込の信頼性に影響するため、初期実装の完了前に修正が必要である。

---

# 3. 指摘事項

## REV-001: 固定のセマンティック説明によって日付と合計金額が読み上げられない

* **重要度**: `Medium`
* **カテゴリ**: `Bug`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Home/HomePage.xaml`、`src/TkpSalaryCalculator.App/Presentation/Features/Home/HomeDestinationPage.xaml`
* **対象箇所**: `HomePage.xaml` 23～28行、67～71行、`HomeDestinationPage.xaml` 20～23行
* **修正要否**: `Required`

### 問題

給与算定開始日、給与算定終了日、給与見込み合計、および遷移先の対象給与期間を表示する`Label`に、値を含まない固定文字列の`SemanticProperties.Description`を設定している。そのためスクリーンリーダーでは、画面に表示された日付・金額・給与期間ではなく「給与算定開始日」「給与見込み合計」などの固定説明だけが読み上げられる。

### 根拠

`docs/screen_specification.md` 492行は、スクリーンリーダーで金額を金額として読み上げられるラベルを要求している。`docs/test_specification.md`の`A11Y-002`も、ラベルと値を読み上げ可能であることを合格条件としている。

.NET MAUI公式ドキュメントは、`Label`へ`SemanticProperties.Description`を設定すると`Text`がスクリーンリーダーで読み上げられなくなるため、設定を避けるよう明記している。

### 現在の状態

例えば合計欄では、動的な金額は`Text`にのみ存在し、読み上げに使われる説明は固定されている。

```text
SemanticProperties.Description="給与見込み合計"
Text="{Binding TotalText}"
```

開始日・終了日および遷移先の給与期間にも同じ構造がある。現在のApp層テストはXAML内のバインディング名と一部の説明属性の存在だけを確認しており、読み上げ内容を検証していない。

### 期待する状態

スクリーンリーダー利用時にも、項目名と現在表示中の値を組み合わせて、開始日、終了日、給与見込み合計、対象給与期間を確認できる。

### 推奨修正方針

値を表示する`Label`から固定の`SemanticProperties.Description`を外して表示テキストをそのまま読ませるか、項目名と動的な値を含む読み上げ専用プロパティへバインドする。金額については日本円の金額として自然に読める文字列を用意する。修正後はAndroidのTalkBackで実際の読み上げ内容を確認する。

### 修正確認条件

* [ ] 給与算定開始日と終了日が、項目名と日付を含めて読み上げられる
* [ ] 給与見込み合計が、現在の金額と円単位を含めて読み上げられる
* [ ] 遷移先の対象給与期間が値を含めて読み上げられる
* [ ] TalkBackを用いた`A11Y-002`の確認結果が記録されている

---

## REV-002: 画面離脱後すぐに戻ると再読込要求が破棄される

* **重要度**: `Medium`
* **カテゴリ**: `Bug`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Home/HomePage.xaml.cs`、`src/TkpSalaryCalculator.App/Presentation/Features/Home/HomeViewModel.cs`、`src/TkpSalaryCalculator.App/Presentation/Common/ViewModelBase.cs`
* **対象箇所**: `HomePage.OnAppearing` 15～19行、`HomePage.OnDisappearing` 21～24行、`HomeViewModel.LoadAsync` 259～266行、`ViewModelBase.RunBusyAsync` 34～59行
* **修正要否**: `Required`

### 問題

ホームの読込中に別タブなどへ移動し、取消処理が完了する前にホームへ戻ると、2回目の`LoadAsync`は`IsBusy`がまだ`true`であるため何もせず終了する。その後、最初の処理が取消完了しても再読込は開始されず、ホームが空または古い集計のまま残る。

### 根拠

`HomeViewModel.LoadAsync`自身のコメントは、画面を表示するたびに保存後やインポート後の最新状態を読み直すとしている。`docs/screen_specification.md` 497～501行も、選択状態を保持しつつ、インポート後などはホームから最新データを読み直すことを要求している。画面が再表示されたにもかかわらず読込要求を破棄すると、この前提を満たせない。

### 現在の状態

次の順序で再現する。

```text
1. OnAppearingでLoadAsyncを開始し、IsBusy=trueになる
2. OnDisappearingが処理をCancelするが、完了は待たない
3. 取消完了前に再度OnAppearingし、LoadAsyncを呼ぶ
4. RunBusyAsyncが「if (IsBusy) return;」で2回目の読込を破棄する
5. 最初の処理だけが取消終了し、再読込されない
```

既存テストのユースケーススタブは即時完了するため、非同期中断中の取消と再進入を検証していない。

### 期待する状態

ホームが再表示された場合は、直前の取消処理とのタイミングにかかわらず、最後の表示要求に対応する最新の給与期間サマリーとバックアップ状態が最終的に反映される。

### 推奨修正方針

再表示時の読込を単純に破棄せず、進行中処理を取り消した後で最新要求を実行する、または世代番号を持つlatest-wins方式で古い結果だけを破棄する。Page側で非同期取消の完了を待つ設計にする場合も、短時間の離脱・復帰で次の読込が必ず予約される条件を保つ。

### 修正確認条件

* [ ] 読込中に別タブへ移動してすぐ戻っても、最新のホーム集計が表示される
* [ ] 古い取消対象の読込結果が新しい表示状態を上書きしない
* [ ] 実際に中断する`TaskCompletionSource`等のスタブで、取消前後の再進入を自動テストしている
* [ ] 通常の画面再表示、期間移動、エラー再試行の既存動作を破壊していない

---

## REV-003: 未計算件数が0件の期間では件数表示自体が消える

* **重要度**: `Low`
* **カテゴリ**: `Specification`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Home/HomePage.xaml`、`src/TkpSalaryCalculator.App/Presentation/Features/Home/HomeViewModel.cs`
* **対象箇所**: `HomePage.xaml` 91～110行、`HomeViewModel.HasUncalculatedRecords` 239～246行、`HomeViewModel.ApplySummary` 288～301行
* **修正要否**: `Required`

### 問題

未計算件数のラベルが、`HasUncalculatedRecords`を表示条件とする警告枠の中にだけ存在する。件数が0件になると枠全体が非表示になるため、ホームには`0件`が表示されない。

### 根拠

`docs/screen_specification.md` 252～258行は、ホームの表示内容として「未計算の勤務記録数」を条件なしで列挙している。`docs/ui_implementation_plan.md` 131行の完了条件も、期間移動により未計算件数が更新されることを求めている。対象日への遷移は件数がある場合だけでよいが、件数そのものの表示条件は規定されていない。

### 現在の状態

`UncalculatedCountText`は0件でも正しく`0件`へ更新されるが、その親要素が非表示になる。

```text
IsVisible="{Binding HasUncalculatedRecords}"
...
<Label Text="{Binding UncalculatedCountText}" />
```

既存の`UI004_PeriodMovesRefreshDatesTotalsBreakdownAndUncalculatedCountTogether`は0件の期間から4件の期間への更新を確認するが、0件表示の可視性は確認していない。

### 期待する状態

選択中の給与期間について未計算件数が常に表示され、0件の場合は`0件`と確認できる。警告表現と「対象日を見る」操作は1件以上の場合だけ表示または有効化される。

### 推奨修正方針

未計算件数の表示を常設のサマリーへ移し、`HasUncalculatedRecords`は警告スタイルと対象日遷移ボタンの表示条件だけに使う。または0件専用の非警告表示を設ける。

### 修正確認条件

* [ ] 未計算0件の給与期間で`0件`が表示される
* [ ] 1件以上の場合は件数と対象日への操作が表示される
* [ ] 1件以上から0件、0件から1件以上の双方の期間移動で表示が更新される
* [ ] 0件の場合に対象日一覧への遷移を実行できない既存制御が維持される

---

# 4. 指摘なし・確認済み事項

* [x] `HomeViewModel`、`PayrollPeriodHeaderViewModel`、`BackupReminderViewModel`の責務分割
* [x] 給与算定開始日・終了日の年を含む日本語表示
* [x] 合計、基本給与、割増、件数加算、月額手当のDTO値を再計算せず表示する処理
* [x] 前・次・現在の給与期間への移動と、期間変更後のサマリー一括更新
* [x] 期間取得失敗時に直前の表示済みサマリーを保持する処理
* [x] 現地日付を用いた現在給与期間および現在日カレンダーの選択
* [x] バックアップ案内条件のユースケース利用と7日延期
* [x] カレンダー、計算内訳、月額手当、未計算日への対象日／給与期間引き渡し
* [x] Shellルート、DI、App層依存方向
* [x] コマンドの二重実行防止
* [x] `git diff --check 419fdcd..1b772b3`で空白エラーなし
* [x] App層テストプロジェクトの再ビルド成功（警告0、エラー0）
* [x] `TkpSalaryCalculator.App.Tests.dll`のテスト70件成功
* [ ] Android XAMLを含むアプリビルド（ローカルJDK未設定により`XA5300`で未確認）
* [ ] Android実機／エミュレーターでのTalkBack、最大文字サイズ、短時間のタブ離脱・復帰

# 5. 修正側AIへの引き継ぎ

以下のルールで修正すること。

1. `Required` の指摘を優先して修正する。
2. 各 `REV-xxx` を独立した修正事項として扱う。
3. 指摘内容だけで判断できない場合は、元の仕様および対象コードを確認する。
4. 指摘の意図を満たす範囲で、既存設計への変更を最小限にする。
5. 指摘されていない箇所を理由なく変更しない。
6. 修正によって既存テストを無効化・削除しない。
7. 修正完了後、各指摘について結果を報告する。

## 修正結果の報告形式

| 指摘ID | 状態 | 対応内容 |
| --- | --- | --- |
| REV-001 | Fixed / Not Fixed / Not Applicable | 修正内容 |
| REV-002 | Fixed / Not Fixed / Not Applicable | 修正内容 |
| REV-003 | Fixed / Not Fixed / Not Applicable | 修正内容 |

`Not Fixed` または `Not Applicable` とした場合は、その理由を記載する。
