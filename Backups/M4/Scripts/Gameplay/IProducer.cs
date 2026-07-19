namespace Game.Gameplay
{
    /// <summary>A station whose production rate a hired manager multiplies (GDD §6).</summary>
    public interface IProducer
    {
        double RateMultiplier { get; set; }
    }
}
