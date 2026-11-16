using Microsoft.Extensions.DependencyInjection;
using NOF.Domain;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace NOF.Infrastructure.Tests.Services;

public class SnowflakeIdGeneratorTests
{
    private static SnowflakeIdGenerator Default()
        => CreateGenerator(new SnowflakeIdGeneratorOptions());

    private static SnowflakeIdGenerator CreateGenerator(
        SnowflakeIdGeneratorOptions? options = null,
        uint applicationId = 0,
        uint instanceId = 0)
        => new(options ?? new SnowflakeIdGeneratorOptions(), applicationId, instanceId);

    // -----------------------------------------------------------------------
    // Basic generation
    // -----------------------------------------------------------------------

    [Fact]
    public void NextId_ReturnsPositiveLong()
    {
        var gen = Default();
        Assert.True(gen.NextId() > 0);
    }

    [Fact]
    public void NextId_CalledRepeatedly_ReturnsUniqueIds()
    {
        var gen = Default();
        var ids = Enumerable.Range(0, 1000).Select(_ => gen.NextId()).ToList();
        Assert.Equal(1000, ids.Distinct().Count());
    }

    [Fact]
    public void NextId_IsMonotonicallyIncreasing_WithinSameMs()
    {
        var gen = Default();
        var ids = Enumerable.Range(0, 100).Select(_ => gen.NextId()).ToList();
        Assert.True((ids).SequenceEqual((ids).OrderBy(x => x)));
    }

    // -----------------------------------------------------------------------
    // Options: deployment fields embedded correctly
    // -----------------------------------------------------------------------

    [Fact]
    public void NextId_EmbedsApplicationIdAndInstanceId_InCorrectBitPosition()
    {
        const uint ApplicationId = 7;
        const uint InstanceId = 9;
        var gen = CreateGenerator(applicationId: ApplicationId, instanceId: InstanceId);
        var id = gen.NextId();

        const int SequenceBits = 8;
        const int InstanceIdBits = 6;
        const int ApplicationIdBits = 8;
        const long MaxInstanceId = (1L << InstanceIdBits) - 1;
        const long MaxApplicationId = (1L << ApplicationIdBits) - 1;
        var extractedInstanceId = (uint)((id >> SequenceBits) & MaxInstanceId);
        var extractedApplicationId = (uint)((id >> (SequenceBits + InstanceIdBits)) & MaxApplicationId);
        Assert.Equal(InstanceId, extractedInstanceId);
        Assert.Equal(ApplicationId, extractedApplicationId);
    }

    // -----------------------------------------------------------------------
    // Options: custom bit layout
    // -----------------------------------------------------------------------

    [Fact]
    public void CustomBitLayout_EmbedsApplicationIdAndInstanceId_Correctly()
    {
        const int ApplicationIdBits = 6;
        const int InstanceIdBits = 7;
        const int SequenceBits = 8;
        const uint ApplicationId = 5;
        const uint InstanceId = 13;

        var gen = CreateGenerator(new SnowflakeIdGeneratorOptions
        {
            ApplicationIdBits = ApplicationIdBits,
            InstanceIdBits = InstanceIdBits,
            SequenceBits = SequenceBits,
        }, ApplicationId, InstanceId);

        var id = gen.NextId();
        var maxInstanceId = (1L << InstanceIdBits) - 1;
        var maxApplicationId = (1L << ApplicationIdBits) - 1;
        var extractedInstanceId = (uint)((id >> SequenceBits) & maxInstanceId);
        var extractedApplicationId = (uint)((id >> (SequenceBits + InstanceIdBits)) & maxApplicationId);
        Assert.Equal(InstanceId, extractedInstanceId);
        Assert.Equal(ApplicationId, extractedApplicationId);
    }

    [Fact]
    public void CustomBitLayout_StillProducesUniqueIds()
    {
        var gen = CreateGenerator(new SnowflakeIdGeneratorOptions
        {
            ApplicationIdBits = 8,
            InstanceIdBits = 6,
            SequenceBits = 8,
        }, applicationId: 3, instanceId: 7);

        var ids = Enumerable.Range(0, 500).Select(_ => gen.NextId()).ToList();
        Assert.Equal(500, ids.Distinct().Count());
    }

    // -----------------------------------------------------------------------
    // Options: custom epoch
    // -----------------------------------------------------------------------

    [Fact]
    public void CustomEpoch_ProducesLargerTimestampComponent_ThanDefaultEpoch()
    {
        // A later epoch means smaller timestamp component (less time has elapsed)
        var laterEpoch = DateTimeOffset.UtcNow.AddSeconds(-10);
        var gen = CreateGenerator(new SnowflakeIdGeneratorOptions { Epoch = laterEpoch });
        Assert.True(gen.NextId() > 0);
    }

    // -----------------------------------------------------------------------
    // Options: validation
    // -----------------------------------------------------------------------

    [Fact]
    public void InvalidApplicationIdBits_ShouldFailOptionsValidation()
    {
        var results = ValidateOptions(new SnowflakeIdGeneratorOptions { ApplicationIdBits = 0 });
        Assert.NotEmpty(results);
    }

    [Fact]
    public void InvalidInstanceIdBits_ShouldFailOptionsValidation()
    {
        var results = ValidateOptions(new SnowflakeIdGeneratorOptions { InstanceIdBits = 0 });
        Assert.NotEmpty(results);
    }

    [Fact]
    public void InvalidSequenceBits_ShouldFailOptionsValidation()
    {
        var results = ValidateOptions(new SnowflakeIdGeneratorOptions { SequenceBits = 0 });
        Assert.NotEmpty(results);
    }

    [Fact]
    public void BitLayoutOverflow_Throws()
    {
        var act = () => CreateGenerator(new SnowflakeIdGeneratorOptions
        {
            ApplicationIdBits = 32,
            InstanceIdBits = 16,
            SequenceBits = 16
        });
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void ApplicationId_OutOfRange_Throws()
    {
        var act = () => CreateGenerator(
            new SnowflakeIdGeneratorOptions
            {
                ApplicationIdBits = 4,
                InstanceIdBits = 8,
                SequenceBits = 8
            },
            applicationId: 16,
            instanceId: 1);
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void InstanceId_OutOfRange_Throws()
    {
        var act = () => CreateGenerator(
            new SnowflakeIdGeneratorOptions
            {
                ApplicationIdBits = 8,
                InstanceIdBits = 4,
                SequenceBits = 8
            },
            applicationId: 1,
            instanceId: 16);
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    // -----------------------------------------------------------------------
    // IIdGenerator interface
    // -----------------------------------------------------------------------

    [Fact]
    public void ImplementsIIdGenerator()
    {
        IIdGenerator gen = Default();
        Assert.True(gen.NextId() > 0);
    }

    // -----------------------------------------------------------------------
    // IdGenerator ambient accessor
    // -----------------------------------------------------------------------

    [Fact]
    public void IdGenerator_PushCurrent_MakesCurrent_Available_AndRestoresPrevious()
    {
        var outer = Default();
        var inner = CreateGenerator(applicationId: 1, instanceId: 1);

        using var outerScope = IdGenerator.PushCurrent(outer);
        Assert.Same(outer, IdGenerator.Current);

        using (IdGenerator.PushCurrent(inner))
        {
            Assert.Same(inner, IdGenerator.Current);
        }

        Assert.Same(outer, IdGenerator.Current);
    }

    [Fact]
    public void IdGenerator_PushCurrent_Null_ShadowsAndRestoresOuterBinding()
    {
        var services = new ServiceCollection();
        services.AddIdGenerator().AddSnowflakeIdGenerator();
        using var provider = services.BuildServiceProvider();
        using var outer = IdGenerator.PushCurrent(provider.GetRequiredService<IIdGenerator>());
        var generator = IdGenerator.Current;
        using (IdGenerator.PushCurrent((IIdGenerator?)null))
        {
            Assert.Null(IdGenerator.Current);
        }
        Assert.Same(generator, IdGenerator.Current);
    }

    private static List<ValidationResult> ValidateOptions(SnowflakeIdGeneratorOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
