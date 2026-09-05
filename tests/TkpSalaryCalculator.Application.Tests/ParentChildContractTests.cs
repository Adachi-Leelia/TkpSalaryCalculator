using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Tests;

public sealed class ParentChildContractTests
{
    [Fact]
    public void WorkRecordDtoConvertsAllTasksToDomainWithoutOriginMetadata()
    {
        var recordId = new WorkRecordId(Guid.NewGuid());
        var firstId = new WorkTaskId(Guid.NewGuid());
        var secondId = new WorkTaskId(Guid.NewGuid());
        var presetId = new ServicePresetId(Guid.NewGuid());
        var dto = new WorkRecordDto(
            recordId,
            new DateOnly(2026, 8, 15),
            [
                new WorkTaskDto(firstId, TestData.ServiceId, TestData.CategoryId,
                    WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0), presetId),
                new WorkTaskDto(secondId, TestData.ServiceId, TestData.CategoryId,
                    WorkInputMode.Duration, new WorkMinutes(30), null, null, new DisplayOrder(1), null),
            ],
            new BasicShiftId(Guid.NewGuid()),
            new WorkRecordId(Guid.NewGuid()));

        var domain = dto.ToDomain();

        Assert.Equal(recordId, domain.Id);
        Assert.Equal([firstId, secondId], domain.Tasks.Select(task => task.Id));
        Assert.Equal([0, 1], domain.Tasks.Select(task => task.DisplayOrder.Value));
        Assert.DoesNotContain(
            typeof(TkpSalaryCalculator.Domain.Models.WorkTask).GetProperties(),
            property => property.Name.Contains("Source", StringComparison.Ordinal));
    }

    [Fact]
    public void SaveCommandsCarryStableChildIdentifiersAndOrder()
    {
        var workTaskId = new WorkTaskId(Guid.NewGuid());
        var shiftTaskId = new BasicShiftTaskId(Guid.NewGuid());
        var work = new SaveWorkRecordCommand(
            null,
            new DateOnly(2026, 8, 15),
            [new SaveWorkTaskCommand(workTaskId, TestData.ServiceId, TestData.CategoryId,
                WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0), null)],
            Guid.NewGuid());
        var shift = new SaveBasicShiftCommand(
            null,
            DayOfWeek.Saturday,
            [new SaveBasicShiftTaskCommand(shiftTaskId, null, TestData.ServiceId, TestData.CategoryId,
                WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))],
            new DisplayOrder(0),
            true);

        Assert.Equal(workTaskId, Assert.Single(work.Tasks).Id);
        Assert.Equal(shiftTaskId, Assert.Single(shift.Tasks).Id);
    }

    [Fact]
    public void BasicShiftDtoSeparatesParentAndTaskDisplayOrder()
    {
        var shiftTask = new BasicShiftTaskDto(
            new BasicShiftTaskId(Guid.NewGuid()),
            null,
            TestData.ServiceId,
            TestData.CategoryId,
            WorkInputMode.Duration,
            new WorkMinutes(60),
            null,
            null,
            new DisplayOrder(0));
        var shift = new BasicShiftDto(
            new BasicShiftId(Guid.NewGuid()),
            DayOfWeek.Saturday,
            [shiftTask],
            new DisplayOrder(5),
            true);

        Assert.Equal(5, shift.DisplayOrder.Value);
        Assert.Equal(0, Assert.Single(shift.Tasks).DisplayOrder.Value);
    }

    [Fact]
    public void WorkContractsKeepTasksAsTheSingleSourceOfTruth()
    {
        var recordId = new WorkRecordId(Guid.NewGuid());
        var operationId = Guid.NewGuid();
        var replacementServiceId = new ServiceId(Guid.NewGuid());
        var replacementTask = new WorkTaskDto(
            new WorkTaskId(Guid.NewGuid()),
            replacementServiceId,
            null,
            WorkInputMode.Duration,
            new WorkMinutes(30),
            null,
            null,
            new DisplayOrder(0),
            null);
        var record = new WorkRecordDto(
            recordId,
            new DateOnly(2026, 8, 15),
            [replacementTask with { ServiceId = TestData.ServiceId }],
            null,
            null) with
        {
            Tasks = [replacementTask],
        };
        var command = new SaveWorkRecordCommand(
            recordId,
            record.WorkDate,
            [new SaveWorkTaskCommand(replacementTask.Id, replacementServiceId, null,
                WorkInputMode.Duration, new WorkMinutes(30), null, null, new DisplayOrder(0), null)],
            operationId);

        Assert.Equal(replacementServiceId, record.ServiceId);
        Assert.Equal(replacementServiceId, Assert.Single(record.ToDomain().Tasks).ServiceId);
        Assert.Equal(replacementServiceId, Assert.Single(command.Tasks).ServiceId);
        Assert.Equal(command, command with { Tasks = command.Tasks.ToArray() });
        Assert.Equal(command.GetHashCode(), (command with { Tasks = command.Tasks.ToArray() }).GetHashCode());
    }

    [Fact]
    public void BasicShiftTasksAreTheSingleSourceOfTruth()
    {
        var shiftId = new BasicShiftId(Guid.NewGuid());
        var replacementServiceId = new ServiceId(Guid.NewGuid());
        var replacementTask = new BasicShiftTaskDto(
            new BasicShiftTaskId(Guid.NewGuid()),
            null,
            replacementServiceId,
            null,
            WorkInputMode.Duration,
            new WorkMinutes(30),
            null,
            null,
            new DisplayOrder(0));
        var shift = new BasicShiftDto(
            shiftId,
            DayOfWeek.Saturday,
            [replacementTask with { ServiceId = TestData.ServiceId }],
            new DisplayOrder(0),
            true) with
        {
            Tasks = [replacementTask],
        };
        var command = new SaveBasicShiftCommand(
            shiftId,
            DayOfWeek.Saturday,
            [new SaveBasicShiftTaskCommand(replacementTask.Id, null, TestData.ServiceId, null,
                WorkInputMode.Duration, new WorkMinutes(30), null, null, new DisplayOrder(0))],
            new DisplayOrder(0),
            true) with
        {
            Tasks = [new SaveBasicShiftTaskCommand(replacementTask.Id, null, replacementServiceId, null,
                WorkInputMode.Duration, new WorkMinutes(30), null, null, new DisplayOrder(0))],
        };

        Assert.Equal(replacementServiceId, Assert.Single(shift.Tasks).ServiceId);
        Assert.Equal(replacementServiceId, Assert.Single(command.Tasks).ServiceId);
        Assert.Equal(shift, shift with { Tasks = shift.Tasks.ToArray() });
        Assert.Equal(shift.GetHashCode(), (shift with { Tasks = shift.Tasks.ToArray() }).GetHashCode());
    }
}
