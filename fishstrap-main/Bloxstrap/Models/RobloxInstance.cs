namespace Bloxstrap.Models
{
	public class RobloxInstance
	{
		public int ProcessId { get; set; }
		public long WindowHandle { get; set; }

        /// <summary>
        /// The user ID of the managed account this process was launched as,
        /// or 0 if unknown.
        /// </summary>
        public long AccountUserId { get; set; }
    }
}