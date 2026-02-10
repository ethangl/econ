namespace EconSim.Core.Diagnostics
{
    public interface IDomainLogSink
    {
        void Write(DomainLogEvent entry);
    }
}
