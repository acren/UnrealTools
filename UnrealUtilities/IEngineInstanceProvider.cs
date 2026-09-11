using Newtonsoft.Json;

namespace UnrealUtilities
{
    public interface IEngineInstanceProvider
    {
        [JsonIgnore] public Engine EngineInstance { get; }

        //[JsonIgnore] public string EngineInstanceName => EngineInstance.Name;
    }
}
