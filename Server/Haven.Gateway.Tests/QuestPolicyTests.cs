using Haven.Gateway.Contracts;
using Haven.Gateway.Services;
using Xunit;

namespace Haven.Gateway.Tests;

public sealed class QuestPolicyTests
{
    private static QuestGenerateRequest ValidRequest() => new(
        "request-1",
        "camp_guide",
        "营地需要什么？",
        new QuestContext(1, ["Wood", "Stone"], ["Food"]));

    [Fact]
    public void ValidQuest_PassesPolicy()
    {
        var quest = new DeepSeekQuestPayload("请收集木材。", "Collect", "Wood", 3, "Food");

        Assert.Null(QuestPolicy.ValidateRequest(ValidRequest()));
        Assert.Null(QuestPolicy.ValidateGenerated(quest, ValidRequest()));
    }

    [Theory]
    [InlineData("Destroy", "Wood", 3, "Food")]
    [InlineData("Collect", "Gold", 3, "Food")]
    [InlineData("Collect", "Wood", 0, "Food")]
    [InlineData("Collect", "Wood", 3, "Gem")]
    public void OutOfPolicyQuest_IsRejected(string type, string target, int count, string reward)
    {
        var quest = new DeepSeekQuestPayload("测试", type, target, count, reward);

        Assert.NotNull(QuestPolicy.ValidateGenerated(quest, ValidRequest()));
    }

    [Fact]
    public void EmptyPlayerMessage_IsRejected()
    {
        var request = ValidRequest() with { PlayerMessage = "" };

        Assert.NotNull(QuestPolicy.ValidateRequest(request));
    }
}
