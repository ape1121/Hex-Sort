namespace Ape.Core
{
    public interface IManager
    {
        void Initialize();
        void Tick(float deltaTime) {}
        void Shutdown() {}
    }
}
