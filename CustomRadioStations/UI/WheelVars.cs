using SelectorWheel;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomRadioStations {
    public static class WheelVars {
        public static List<Wheel> RadioWheels = new List<Wheel>();

        public static Wheel CurrentRadioWheel;

        public static Wheel NextQueuedWheel;
    }
}