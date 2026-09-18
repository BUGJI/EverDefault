namespace EverDefault.Registry.UserChoice
{
    public sealed class RestoreResult
    {
        public bool ValuesRestored { get; private set; }

        public bool LastWriteTimeRestored { get; private set; }

        public string Error { get; private set; }

        public bool Success
        {
            get { return ValuesRestored && Error == null; }
        }

        private RestoreResult()
        {
        }

        public static RestoreResult Create(bool valuesRestored, bool timeRestored, string error)
        {
            return new RestoreResult
            {
                ValuesRestored = valuesRestored,
                LastWriteTimeRestored = timeRestored,
                Error = error
            };
        }

        public override string ToString()
        {
            return string.Format(
                "ValuesRestored={0}, LastWriteTimeRestored={1}, Error={2}",
                ValuesRestored,
                LastWriteTimeRestored,
                Error ?? "-");
        }
    }
}
