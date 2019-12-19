
using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Grape
{
	public class TraceGeneratorConfiguration
	{
    	public int duration { get; set; }
    	public IList<EventProvider> eventProviders { get; set; }
    	public string traceName { get; set; }
	}

	public class EventProvider
	{
	    public string Name { get; set; }
	    public int Level { get; set; }
	    public string Keywords { get; set; } // Essentially this should be a string representation of a hex number
	    public string Arguments { get; set; }
	}
}
