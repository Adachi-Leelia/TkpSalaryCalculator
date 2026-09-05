# Application test specification coverage

This table is the traceability source for every test in this project. IDs refer to
`docs/test_specification.md`; `APP-*` identifies Application contract, validation,
or orchestration coverage that complements the end-to-end specification case.

## ArchitectureTests

| Test | Specification |
|---|---|
| `ApplicationProject_ReferencesOnlyDomainProjectAndNoPlatformPackages` | ARCH-002 |
| `ApplicationAssemblyAndSource_UseOnlyAllowedDependenciesAndNamespaces` | ARCH-002 |

## WorkRecordUseCaseTests

| Test | Specification |
|---|---|
| `Preview_TimeRange_NormalizesOverMidnight_WithoutWrites` | WORK-003 |
| `Preview_DurationWithTimedPremium_RequiresStartAndDerivesEnd` | WORK-005 |
| `Preview_TimedPremiumForOtherWeekday_DoesNotRequireStart` | WORK-004, WORK-005 |
| `PreviewCopyDay_DisabledTargetService_IsBlocking` | WORK-010, HIST-012 |
| `Preview_ZeroMinutes_CannotSave` | WORK-006, WORK-014 |
| `Save_MissingRate_IsPersistedAsUncalculated` | WORK-002, CALC-012 |
| `Save_ConcurrentSameCommand_PersistsOnce` | WORK-007 |
| `Save_CancellationOfOneWaiter_DoesNotCancelSharedOperation` | WORK-007, APP-CANCEL |
| `Save_SameOperationAcrossUseCaseInstances_ReturnsPersistedResult` | WORK-007 |
| `Save_RepositoryFailure_DoesNotCommitOrMarkChanged` | WORK-008 |
| `CopyDay_SecondInsertFailure_RollsBackEntireUnitOfWork` | WORK-008, WORK-010 |
| `CopyDay_CreatesIndependentIdsAndUsesTargetMonth` | WORK-010, HIST-003 |
| `Save_EditPreservesShiftAndCopyProvenance` | HIST-008, WORK-013, SHIFT-007 |
| `Save_ReusedOperationIdWithDifferentInput_IsRejectedPendingAndPersisted` | WORK-007 |
| `GetForDateAndDelete_ReflectStoredRecords` | WORK-009, APP-WORK-QUERY |
| `Preview_RejectsInvalidModeAndOverTwentyFourHours_AndHonorsCancellation` | WORK-006, WORK-014, APP-CANCEL |
| `GetInputOptions_OrdersByConfiguredDisplayOrderWithoutReadingWorkHistory` | WORK-001, WORK-012 |
| `Save_MultipleTasks_NormalizesOrderAndAppliesCountBonusOnce` | WORK-016, WORK-018, CALC-014 |
| `Preview_MultipleTasks_PointsInputAndCalculationIssuesToStableTaskPaths` | WORK-014, WORK-019, CALC-016 |
| `Save_RetryComparesEveryTaskInPayload` | WORK-007, WORK-019 |
| `Save_EditMultipleTasks_ReplacesVisitAndPreservesTaskIds` | WORK-016, WORK-020 |
| `CopyDay_MultipleTasks_AssignsNewParentAndTaskIdsAndPreservesOrder` | WORK-020 |

## BasicShiftUseCaseTests

| Test | Specification |
|---|---|
| `GetForWeekday_ReturnsDisplayOrder` | SHIFT-001 |
| `Preview_DoesNotPersistAndWarnsSimilarManualRecord` | SHIFT-002, SHIFT-003, SHIFT-006 |
| `Apply_PersistsEachSelectedShiftAsIndependentWork` | SHIFT-004 |
| `Apply_AlreadyAppliedShift_IsRejected` | SHIFT-005 |
| `Preview_DisabledOrUnavailableShift_IsNotApplicable` | SHIFT-009 |
| `Preview_MissingRate_IsNotApplicable` | SHIFT-009, CALC-012 |
| `Apply_FailureInBatch_DoesNotCommit` | SHIFT-010 |
| `Apply_SecondInsertFailure_RollsBackAllRecords` | SHIFT-010 |
| `Save_CreatesAndUpdatesShift_AndRejectsInvalidWeekday` | SHIFT-001, SHIFT-007, APP-VALIDATION |
| `UpdatingOrDeletingShift_DoesNotModifyExistingWork` | SHIFT-007, SHIFT-008 |
| `Save_MultipleTasks_NormalizesOrderAndIndependentTimes` | SHIFT-001, SHIFT-011 |
| `Save_RejectsEmptyDuplicateOrInvalidTasksWithoutWriting` | SHIFT-011, DB-021 |
| `Apply_MultipleTasks_CountsVisitOnceAndKeepsTasksAfterSourceChanges` | SHIFT-004, SHIFT-005, SHIFT-007, SHIFT-008, SHIFT-013, CALC-014 |
| `Preview_SimilarityComparesCompleteTaskMultiset` | SHIFT-006, SHIFT-012 |
| `Preview_SecondTaskFailureBlocksVisitAndIdentifiesTask` | SHIFT-009, CALC-016 |

## SettingsAndSalaryUseCaseTests

| Test | Specification |
|---|---|
| `CloneAndReplace_ChangesOnlyTargetMonthAndMarksChanged` | HIST-001, HIST-002 |
| `PreviewReplacement_ReportsAffectedRecordsAndHasNoSideEffects` | HIST-013 |
| `PreviewReplacement_CountsAffectedVisitOnceWhenAllOfItsTasksChange` | HIST-017 |
| `PreviewReplacement_NullChild_ReturnsSafeValidationIssue` | APP-VALIDATION |
| `CopyPreviousMonth_UsesLatestHolidayAndKeepsOtherMonths` | HIST-009, HIST-014 |
| `CloneFailure_DoesNotCommitOrMarkChanged` | HIST-004 |
| `Clone_MetadataFailure_RollsBackSnapshotReferenceAndMetadata` | HIST-004 |
| `CloneAndReplace_RejectsStalePreviewAfterWorkChange` | HIST-013, HIST-014 |
| `CloneAndReplace_RepositoryCasFailure_LeavesSettingsAndMetadataUnchanged` | HIST-004, HIST-014 |
| `CloneAndReplace_RejectsTokenReusedForDifferentReplacementOrMonth` | HIST-013, HIST-014 |
| `SalaryQuery_UsesEachWorkDatesCalendarMonthAndAddsAllowanceOnce` | HIST-003, CALC-010 |
| `ANNUALAPP001_HomeSummaryBatchesAnnualRangeAndBuildsMonthlyFromTheSameRead` | ANNUAL-001, ANNUAL-004, ANNUAL-006, ANNUAL-008, ANNUAL-010, DB-015 |
| `ANNUALAPP002_HomeSummaryIncludesAllowanceOnlyPeriodsAndReturnsZeroWithoutData` | ANNUAL-007, ANNUAL-008 |
| `ANNUALAPP003_HomeSummaryUsesClosingHistoryWithoutBoundaryGapsOrDuplicates` | ANNUAL-005, ANNUAL-010 |
| `PERF010_MultipleTasksKeepCalendarPeriodAndAnnualReadsBatchedByRange` | PERF-010 |
| `CalendarDayAndMonthQueries_UseOrchestratedApplicationModels` | APP-SALARY-QUERY, SHIFT-002 |
| `WorkRecordCalculationConvertsEveryTaskFromTheParentChildContract` | HIST-016, APP-SALARY-QUERY |
| `ClosingRuleReplacement_PreservesHistoryAndAllowanceIsPeriodScoped` | CALC-009, CALC-010 |
| `FirstClosingRule_IsPersistedAsBaselineAndUsedForPeriodCalculation` | CALC-009 |
| `FindPayrollPeriod_UsesClosingDayBoundaryForCurrentDate` | UI-007, CALC-009 |
| `FindPayrollPeriod_RequiresClosingRuleHistory` | UI-002, CALC-009 |
| `ClosingRuleCommit_RejectsStaleHistoryVersion` | HIST-004, CALC-009 |
| `ClosingRuleToken_CannotBeReusedForDifferentDayWithSameHistoryVersion` | HIST-004, CALC-009 |
| `ClosingRuleRepositoryCasFailure_DoesNotChangeState` | HIST-004, CALC-009 |
| `ClosingRule_MetadataFailure_RollsBackHistoryAndVersion` | HIST-004, CALC-009 |
| `SettingsClosingAndAllowanceReadDelete_PublicOperationsWork` | CALC-009, CALC-010, APP-SETTINGS-QUERY |

## SetupBackupDataTransferTests

| Test | Specification |
|---|---|
| `InitialSetup_ResumesAndCompletesOnlyWithRequiredSettings` | UI-002, UI-003, UX-004 |
| `InitialSetup_MissingClosingRule_DoesNotComplete` | UI-002 |
| `InitialSetup_EnabledServiceWithoutApplicableRate_DoesNotComplete` | UI-002, CALC-012 |
| `InitialSetup_RejectsBlankAndLongStep_AndHonorsCancellation` | APP-VALIDATION, APP-CANCEL |
| `ServicePreset_AllOperationsAndValidation_AreOrchestrated` | WORK-001, WORK-013, WORK-014 |
| `BackupReminder_ShowsForNeverExportedAndDefersSevenDays` | UX-007, APP-BACKUP |
| `BackupReminder_ChangedAfterExport_WaitsThirtyDays` | APP-BACKUP |
| `BackupReminder_UsesLocalDateAtUtcDayBoundary` | APP-BACKUP |
| `DataTransfer_PrepareCommit_UsesStagingAndAtomicReplacement` | DATA-002, DATA-007 |
| `DataTransfer_InvalidInput_DiscardsStagingAndDoesNotReplace` | DATA-003, DATA-005, DATA-012 |
| `DataTransfer_CleanupFailure_DoesNotReplaceOriginalFailureOrCommittedSuccess` | DATA-007, DATA-012 |
| `DataTransfer_DiscardAndDoubleCommit_PreserveLiveDataAndEnforceTokenState` | DATA-006, DATA-012 |
| `DataTransfer_ReplacementFailure_RollsBackLiveDataAndKeepsValidatedStage` | DATA-007 |
| `DataTransfer_CancelledPrepareDeletesTemporaryData_AndNextPrepareCleansAbandoned` | DATA-006, DATA-012 |
| `Export_StreamsRecordsAndOnlyUpdatesExportTimestamp` | DATA-001 |
| `DataTransfer_FormatAndPublicArgumentValidation_AreStable` | DATA-004, DATA-008, APP-VALIDATION |
| `Export_UsesFixedSnapshot_WritesNonSeekableStream_AndClosesOwnedDestination` | DATA-001, DATA-008 |
| `Export_DestinationCloseFailureDoesNotRecordSuccessfulExport` | DATA-001, FR-MSG-01 |
| `Export_FailureAndCancellation_DisposeSnapshotWithoutUpdatingTimestamp` | DATA-001, DATA-008, APP-CANCEL |
