# Phase 2 入力候補索引 A/B 計測記録

## 位置づけ

`response-performance-plan.md` の Phase 2 / PERF-03 に対する索引採否記録である。これは開発PC上の診断計測であり、代表Android実機での Phase 0 性能ゲートを代替しない。

## 条件

- 実施日: 2026-08-23
- 環境: Windows 10.0.26200、.NET SDK 10.0.400、Microsoft.Data.Sqlite
- データ: `work_record` 219,000件、サービスプリセット20件、1日20件
- 接続設定: WAL、`synchronous=FULL`
- 読取: 3回ウォームアップ後に10回計測し、中央値と最悪値を記録
- 書込: 219,000件一括投入、30件の個別保存、30件の個別更新

## 採用索引

```sql
CREATE INDEX ix_work_record_source_preset
ON work_record(source_service_preset_id)
WHERE source_service_preset_id IS NOT NULL;
```

代表実行の結果は次のとおり。

| 項目 | 索引なし | 採用索引あり | 差 |
| --- | ---: | ---: | ---: |
| 使用回数`GROUP BY` 中央値 | 98.995ms | 19.104ms | -80.7% |
| 使用回数`GROUP BY` 最悪値 | 100.032ms | 20.619ms | -79.4% |
| 最新行取得 中央値 | 63.761ms | 63.608ms | ほぼ同じ |
| 30件の個別保存 | 94.317ms | 90.810ms | 計測ばらつき内 |
| 30件の個別更新 | 38.998ms | 33.873ms | 計測ばらつき内 |
| 219,000件一括投入 | 1,574.676ms | 1,590.444ms | +1.0% |
| DB容量 | 72,884,224 bytes | 83,886,080 bytes | +15.1% |

`EXPLAIN QUERY PLAN` は、索引なしではテーブル走査と一時B-treeによる`GROUP BY`、索引ありでは `ix_work_record_source_preset` の covering index 使用を示した。読取改善に対して一括投入と個別書込の悪化は小さく、容量増加を許容して採用する。

## 不採用索引

最新行取得用の完全複合索引 `(updated_at_utc DESC, work_date DESC, id DESC)` は、最新行中央値を63.761msから0.012msへ短縮した一方、単独でDB容量を72,884,224 bytesから110,362,624 bytesへ約51.4%増加させ、一括投入も代表実行で約10.0%増加した。入力候補ランキングを勤務入力画面だけに限定した後の残余コストに対して容量負担が大きいため、Phase 2 では採用しない。

## 未完了の実機ゲート

代表Android実機、Release APK、DATA-LARGEおよび61日・1,220件集中ケースによる Phase 0 の5操作、保存・更新・インポート時間、端末上のDB容量は別途再測定が必要である。実機で2秒条件または退行条件を満たさない場合は、索引採否を再評価する。
