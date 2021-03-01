namespace AI {
    public interface IState {

        public void OnEnter();

        public void Tick();

        public void OnExit();
    }
}