# コードレビュー結果

## 1. レビュー情報

* **レビュー対象**: 初期設定（`SCR-INIT-01` ～ `SCR-INIT-05`）の初期実装
* **レビュー日時**: 2026-08-21
* **レビュー回数**: 1
* **レビュー対象ブランチ / コミット**: `develop` / `fe8b260`（比較元: `ffcfeb2`、確認時HEAD: `957ae35`）
* **参照した仕様**: `docs/ui_implementation_plan.md` 111～119行、`docs/requirements.md`、`docs/screen_specification.md`、`docs/default_setting.md`、`docs/setting_history_data_model.md`、`docs/database_specification.md`、`docs/test_specification.md`、`docs/adr/0001-ui-framework.md`
* **レビュー範囲**: `fe8b260` で追加・変更された初期設定のPage、ContentView、ViewModel、App層テスト、および直接利用するApplication／Infrastructure層の契約と永続化処理
* **レビュー対象外**: ホーム以降の未実装画面、Android実機での見た目・ソフトキーボード・戻る操作、署名APK。AndroidビルドはJDK未設定（`XA5300`）のため未完了

## 2. 総合結果

**判定**: `NEEDS_FIX`

### サマリー

* Critical: 0件
* High: 1件
* Medium: 4件
* Low: 2件

### 総評

初期設定の5ステップ、進捗保存、DTOによる完了可否判定、完了後のホーム遷移、および加算を後回しにする導線は実装され、App層テストスイート49件も成功した。一方、非同期処理後にUIバインド状態をバックグラウンドスレッドから更新し得る実装が初期化・保存・確認の全経路にあり、端末上の安定動作を保証できない。また、新規サービス設定の再保存によるプリセット重複、仕様で必要な編集操作およびAndroid標準入力UIの不足があるため、完了扱いの前に修正が必要である。

---

# 3. 指摘事項

## REV-001: 非同期処理後の画面状態更新がUIスレッドへ戻らない

* **重要度**: `High`
* **カテゴリ**: `Bug`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Setup/InitialSetupFlowViewModel.cs`
* **対象箇所**: `InitializeCoreAsync` 262～278行、`MoveToAsync` 328～336行、`RefreshConfirmationAsync` 390～417行、`CompleteAsync` 434～450行ほか
* **修正要否**: `Required`

### 問題

Presentation層で`ConfigureAwait(false)`を使用した直後に、`CurrentStep`、`CanComplete`、各サマリー、子ViewModel、および`ObservableCollection`を更新している。非同期処理が実際に中断した場合、これらの更新はUIスレッド外で実行される。

### 根拠

`docs/adr/0001-ui-framework.md` 74行は、DB等の処理を非同期化しつつ、画面状態の更新だけはUIスレッドで行うことを明示している。.NET MAUIのバインド先更新や`ObservableCollection.CollectionChanged`をUIスレッド外から通知すると、端末やタイミングによって例外、表示更新漏れ、または不安定動作につながる。

### 現在の状態

例えば初期化では、ユースケースの完了後にUIコンテキストを復元しないままコレクションとバインドプロパティを更新する。

```text
var settings = await monthSettings.GetAsync(...).ConfigureAwait(false);
Services.Load(settings.Snapshot, presetValues);
Additions.Load(settings.Snapshot);
```

同じ構造がステップ移動、締め日プレビュー、確認画面更新、完了処理にも存在する。現在の単体テストのスタブはほぼ同期完了するため、この問題を検出できない。

### 期待する状態

DB／ユースケース処理はUIスレッドを占有せず、取得結果をViewModelへ反映する処理だけが必ずUIスレッドで実行される。

### 推奨修正方針

Presentation層ではUIコンテキストを維持するか、I/O結果と画面反映を分離して`MainThread`、`Dispatcher`、または注入可能なUIディスパッチャー経由で状態を更新する。`ObservableCollection`の変更と`PropertyChanged`通知を含む全経路を対象にする。

### 修正確認条件

* [ ] 非同期I/O後のViewModelおよび`ObservableCollection`更新がUIスレッドで行われる
* [ ] 実際に非同期中断するスタブを用いたテストが追加されている
* [ ] 初期化、プレビュー、各ステップ保存、確認更新、完了遷移をAndroid端末またはエミュレーターで確認している

---

## REV-002: 新規サービス設定を再保存するとプリセットが重複する

* **重要度**: `Medium`
* **カテゴリ**: `Bug`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Setup/InitialSetupFlowViewModel.cs`
* **対象箇所**: `SetupRateEditorViewModel.PresetId` 549行、`ServiceRatesStepViewModel.SavePresetsAsync` 707～720行、`AddService` 723～730行
* **修正要否**: `Required`

### 問題

新規追加した行は`PresetId = null`で作成され、`IServicePresetUseCase.SaveAsync`が返す採番済みIDをViewModelへ反映していない。そのため、一度保存して加算画面へ進んだ後に戻って再保存すると、同じサービス設定が別IDで追加される。

### 根拠

`ServicePresetUseCase.SaveAsync`はID未指定の場合に毎回新しいGUIDを採番する。`service_preset`テーブルの一意性は主キーIDだけであり、サービスIDと時間区分IDの組に重複防止制約はないため、同一内容の行が実際に複数保存される。サービス設定は勤務入力候補として列挙されるため、重複は利用者にも表示される。

### 現在の状態

```text
await presetUseCase.SaveAsync(new SaveServicePresetCommand(row.PresetId, ...));
```

戻り値を破棄し、`PresetId`も読み取り専用のままである。

### 期待する状態

同じ編集行を何度保存しても、対応する`service_preset`は同じ論理行として更新され、重複しない。

### 推奨修正方針

新規編集行の作成時点で安定IDを保持するか、最初の保存結果のIDを編集行へ反映して以後の保存で再利用する。既存プリセットがない時間区分を読み込んだ場合も同じ規則を適用する。

### 修正確認条件

* [ ] 新規サービス設定を保存後、戻って再保存してもプリセット件数が増えない
* [ ] 新規行と既存プリセット未紐付け行の両方をテストしている
* [ ] 保存後の勤務入力候補に同一サービス設定が重複表示されない

---

## REV-003: 既存サービス種類への設定追加と並べ替えができない

* **重要度**: `Medium`
* **カテゴリ**: `Specification`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Setup/ServiceRatesStepView.xaml`、`src/TkpSalaryCalculator.App/Presentation/Features/Setup/InitialSetupFlowViewModel.cs`
* **対象箇所**: `ServiceRatesStepView` 11～79行、`ServiceRatesStepViewModel.AddService` 723～730行
* **修正要否**: `Required`

### 問題

画面上の「サービス設定を追加」は、選択した既存サービス種類へ時間区分／サービス設定を追加する操作ではなく、常に新しいサービス種類と1行の設定を同時作成する。各サービス内の`Rates`へ行を追加する操作、および初期候補を並べ替える操作がない。

### 根拠

`docs/requirements.md` 147行はサービス種類・時間区分・標準時間の初期値を追加できることを要求し、`docs/default_setting.md` 25行は初期候補の編集、追加、並べ替え、無効化を要求している。`docs/screen_specification.md` 227行もサービス設定の追加と編集を求めている。

### 現在の状態

`AddService`は新しい`ServiceId`と`TimeCategoryId`を必ず同時採番する。例えば既存の「身体介護」へ新しい時間区分を追加しようとしても、その操作手段がなく、同名サービスを新規作成すると重複名検証で保存できない。

### 期待する状態

利用者が既存サービス種類へサービス設定／時間区分を追加でき、初期候補の表示順も調整できる。新しいサービス種類の追加とは操作上区別される。

### 推奨修正方針

サービス種類単位の「設定を追加」コマンドと、必要なら「サービス種類を追加」コマンドを分離する。並べ替えは上下移動などAndroidで誤操作しにくい明示操作を設け、`DisplayOrder`とプリセットの順序へ反映する。

### 修正確認条件

* [ ] 既存サービス種類へ複数の時間区分／サービス設定を追加できる
* [ ] 新しいサービス種類も追加でき、両操作を誤認しない
* [ ] 初期候補を並べ替えて保存・再読込しても順序が維持される

---

## REV-004: 夜間の開始・終了時刻がAndroid標準の時刻選択UIではない

* **重要度**: `Medium`
* **カテゴリ**: `Specification`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Setup/AdditionsStepView.xaml`
* **対象箇所**: 夜間割増の開始・終了時刻入力 60～74行
* **修正要否**: `Required`

### 問題

夜間割増の開始時刻と終了時刻を自由入力の`Entry`で受け取り、利用者に`HH:mm`形式を手入力させている。

### 根拠

`docs/screen_specification.md` 174行は、時刻にAndroid標準の時刻選択UIを使用することを明示している。ADR-0001もAndroidのネイティブコントロールを使い、入力差異と検証範囲を抑える方針である。

### 現在の状態

```text
<Entry Placeholder="22:00" Text="{Binding StartTimeText, Mode=TwoWay}" />
<Entry Placeholder="05:00" Text="{Binding EndTimeText, Mode=TwoWay}" />
```

形式誤りは保存時まで検出されず、端末ロケールやキーボードにも左右される。

### 期待する状態

開始・終了時刻をAndroid標準の時刻選択UIで入力でき、ViewModelには正規化済みの時刻値が渡る。

### 推奨修正方針

.NET MAUIの`TimePicker`または端末標準ダイアログを利用する時刻選択サービスへ置き換える。表示文字列ではなく`TimeOnly`または`MinuteOfDay`相当の値をViewModelで保持する。

### 修正確認条件

* [ ] 開始・終了時刻がAndroid標準の時刻選択UIで入力できる
* [ ] 日付またぎ、開始終了同時刻、未入力時の検証が維持される
* [ ] 代表端末またはエミュレーターで時刻選択を確認している

---

## REV-005: 入力エラー時に最初のエラー項目へ移動できない

* **重要度**: `Medium`
* **カテゴリ**: `Specification`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Setup/InitialSetupFlowViewModel.cs`、`src/TkpSalaryCalculator.App/Presentation/Features/Setup/InitialSetupFlowPage.xaml`
* **対象箇所**: `SaveClosingDayAsync` 339～350行、`ServiceRatesStepViewModel.BuildReplacement` 658～704行、`AdditionsStepViewModel.BuildReplacement` 871～880行、エラー表示 36～46行
* **修正要否**: `Required`

### 問題

入力エラーをページ上部の共通`ErrorMessage`へ表示するだけで、エラー対象フィールドの表示、強調、スクロール、フォーカス移動がない。長いサービス一覧の下部から保存した場合、エラー表示自体も画面外になる可能性がある。

### 根拠

`docs/screen_specification.md` 161行は、入力エラー時に画面へ留まり、最初のエラー項目へフォーカスを移動することを要求している。現状の初期設定固有例外の多くはFieldを持たず、既存の`IssuePresenter`も使用していない。

### 現在の状態

検証失敗は`ApplicationErrorException`として`ViewModelBase`まで伝わり、安全な文字列へ変換されるだけである。個別EntryやPickerにエラー状態を結び付ける仕組みはない。

### 期待する状態

保存失敗時に入力値を保持したまま、最初の不正項目が視認できる位置へ移動し、対象項目と修正内容を利用者が特定できる。

### 推奨修正方針

初期設定の検証エラーへ安定したフィールド識別子を付与し、ViewModelでフィールド別エラーと最初の不正項目を公開する。Page／ContentView側で対象コントロールへのスクロールとフォーカスを行う。

### 修正確認条件

* [ ] 締め日、サービス名、標準時間、単価、加算値、時刻の各エラーで対象項目を示せる
* [ ] 最初のエラー項目が画面内へスクロールされ、フォーカスされる
* [ ] 入力値が失われず、修正後に再保存できる

---

## REV-006: 初期サービス候補の展開状態と一括無効化が仕様を満たさない

* **重要度**: `Low`
* **カテゴリ**: `Specification`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Setup/ServiceRatesStepView.xaml`
* **対象箇所**: サービス・単価一覧 11～73行
* **修正要否**: `Required`

### 問題

すべてのサービス種類と配下の設定行を常時展開し、無効化は各スイッチを個別に操作する必要がある。未使用候補をまとめて無効化する操作もない。

### 根拠

`docs/screen_specification.md` 229行は、初期状態で使用中サービスだけを展開し、使用しない候補をまとめて無効化できることを要求している。

### 現在の状態

`BindableLayout`が全サービスと全`Rates`を無条件表示し、操作はサービス単位／行単位の`Switch`だけである。

### 期待する状態

初期表示は仕様どおり必要な候補へ集中でき、未使用候補を少ない操作でまとめて無効化できる。

### 推奨修正方針

サービス種類に展開状態を追加し、仕様上の初期展開規則を明示する。未入力行または利用者が選択した候補を一括無効化する明示操作を設ける。

### 修正確認条件

* [ ] 初期展開状態が仕様どおりである
* [ ] 使用しない初期候補をまとめて無効化できる
* [ ] 一括操作後も少なくとも1件の計算可能な設定という完了条件を維持する

---

## REV-007: 確認画面から不備のあるステップへ直接戻れない

* **重要度**: `Low`
* **カテゴリ**: `Specification`
* **対象ファイル**: `src/TkpSalaryCalculator.App/Presentation/Features/Setup/SetupConfirmationStepView.xaml`、`src/TkpSalaryCalculator.App/Presentation/Features/Setup/InitialSetupFlowPage.xaml`
* **対象箇所**: 確認画面 9～55行、共通戻るボタン 73～77行
* **修正要否**: `Required`

### 問題

確認画面は不足内容を文字列で表示するだけで、該当ステップへ戻る操作がない。共通の「戻る」は常に1つ前の加算ステップへ移動するため、締め日やサービスの不備でも複数回戻る必要がある。

### 根拠

`docs/screen_specification.md` 246行は、不備がある場合に該当ステップへ戻る操作を表示することを要求している。

### 現在の状態

確認画面の案内は「不備がある項目は『戻る』で修正できます」となっており、不足項目と遷移先の対応を持っていない。

### 期待する状態

各不足項目から、修正すべき締め日またはサービス・単価ステップへ直接移動できる。

### 推奨修正方針

`IssueDto.Code`と初期設定ステップの対応をViewModelで定義し、確認画面へ「締め日を修正」「サービスと単価を修正」などのコマンドを表示する。

### 修正確認条件

* [ ] 各必須設定エラーから該当ステップへ1操作で移動できる
* [ ] 遷移時に`SaveProgressAsync`で移動先が保存される
* [ ] 修正後に確認画面へ戻るとDTOを再取得して完了可否が更新される

---

# 4. 指摘なし・確認済み事項

* [x] `InitialSetupFlowPage`とステップ別ContentView／ViewModelへの分割
* [x] 案内、締め日、サービス・単価、加算、確認の5ステップ順
* [x] ステップ移動時の`SaveProgressAsync`呼び出しと保存済みステップからの再開
* [x] 確認画面での`InitialSetupStateDto.Issues`に基づく完了ボタン制御
* [x] `CompleteAsync`成功後だけホームへルートを切り替える処理
* [x] 「今は設定しない」で割増・件数加算を入力せず無効化できる導線
* [x] 締め日保存前のプレビューと確認トークン付き置換
* [x] サービス・加算設定のプレビュー後に確認トークン付きでスナップショットを置換する処理
* [x] 二重タップ時のコマンド多重実行防止
* [x] `git diff --check ffcfeb2..fe8b260`で空白エラーなし
* [x] App層テストプロジェクトのビルド成功（警告0、エラー0）
* [x] `TkpSalaryCalculator.App.Tests.dll`のテスト49件成功
* [ ] Android XAMLを含むアプリビルド（ローカルJDK未設定により`XA5300`で未確認）
* [ ] Android実機／エミュレーターでの初期設定再開、キーボード、最大文字サイズ、戻る操作

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
| REV-004 | Fixed / Not Fixed / Not Applicable | 修正内容 |
| REV-005 | Fixed / Not Fixed / Not Applicable | 修正内容 |
| REV-006 | Fixed / Not Fixed / Not Applicable | 修正内容 |
| REV-007 | Fixed / Not Fixed / Not Applicable | 修正内容 |

`Not Fixed` または `Not Applicable` とした場合は、その理由を記載する。
