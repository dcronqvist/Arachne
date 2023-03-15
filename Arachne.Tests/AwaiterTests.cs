using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Arachne.Tests;

public class AwaiterTests
{
    private readonly ITestOutputHelper output;

    public AwaiterTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task AwaitTest1()
    {
        var delivery = new DeliveryService<int>();

        int x = 1000;

        var delivered = delivery.TryDeliverToWaiter(5);

        x = await delivery.AwaitDeliveryAsync(2000); // There should be no delivery, so this should timeout and return default(int)

        Assert.False(delivered);
        Assert.Equal(default(int), x);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(300)]
    public async Task AwaitTest2(int data)
    {
        var delivery = new DeliveryService<int>();

        var x = 0;

        _ = Task.Run(async () =>
        {
            x = await delivery.AwaitDeliveryAsync(5000);
        });

        await Task.Delay(1000);

        var delivered = delivery.TryDeliverToWaiter(data);

        await Task.Delay(1000);

        Assert.True(delivered);
        Assert.Equal(data, x);
    }
}