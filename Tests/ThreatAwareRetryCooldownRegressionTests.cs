using NUnit.Framework;
namespace BetterRimAI.Tests
{
    [TestFixture]
    public class ThreatAwareRetryCooldownRegressionTests
    {
        [Test]
        public void ExpiryRevalidatesInsteadOfBlindlyReleasingPersistentDanger()
        {
            var restriction = new ThreatRestriction();
            restriction.Refresh(100, true);
            Assert.That(restriction.NeedsValidation(219), Is.False);
            Assert.That(restriction.NeedsValidation(220), Is.True);
            restriction.Refresh(220, true);
            Assert.That(restriction.Active, Is.True);
            Assert.That(restriction.NeedsValidation(221), Is.False);
            restriction.Refresh(340, false);
            Assert.That(restriction.Active, Is.False);
        }
        [Test]
        public void TickRollbackForcesRevalidation()
        {
            var restriction = new ThreatRestriction();
            restriction.Refresh(5000, true);
            Assert.That(restriction.NeedsValidation(10), Is.True);
            restriction.Refresh(10, false);
            Assert.That(restriction.Active, Is.False);
        }
        [Test]
        public void RestrictionRechecksAtMostEveryTwoGameSeconds()
        {
            Assert.That(ThreatRestriction.RecheckTicks, Is.InRange(60, 120));
        }
    }
}
