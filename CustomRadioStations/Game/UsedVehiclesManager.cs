using GTA;
using System.Collections.Generic;
using System.Linq;

namespace CustomRadioStations {
    internal static class UsedVehiclesManager {
        private static readonly List<UsedVehicle> vehicles = new List<UsedVehicle>();

        public static bool IsUsedVehicle(Vehicle vehicle) {
            return vehicle != null && vehicle.Exists() && vehicles.Exists(x => x.Handle == vehicle.Handle);
        }

        private static UsedVehicle GetFromList(Vehicle vehicle) {
            return vehicles.FirstOrDefault(x => x.Handle == vehicle.Handle);
        }

        private static void AddVehicle(Vehicle vehicle, StationWheelPair pair) {
            if (vehicle == null || !vehicle.Exists())
                return;

            if (vehicles.Count >= 20)
                vehicles.RemoveAt(0);

            vehicles.Add(new UsedVehicle(vehicle, pair));
        }

        public static void UpdateVehicleWithStationInfo(Vehicle vehicle, StationWheelPair pair) {
            if (IsUsedVehicle(vehicle)) {
                UsedVehicle item = GetFromList(vehicle);

                if (pair == null) {
                    item.RadioInfo = null;
                } else {
                    item.RadioInfo = pair;
                }
            } else {
                AddVehicle(vehicle, pair);
            }
        }

        public static void SetLastStationNow(Vehicle vehicle) {
            if (IsUsedVehicle(vehicle)) {
                UsedVehicle item = GetFromList(vehicle);

                if (item.RadioInfo == null)
                    return;

                StationWheelPair pair = StationWheelPair.List.Find(x => x.Equals(item.RadioInfo));
                if (pair == null)
                    return;

                WheelVars.CurrentRadioWheel = pair.Wheel;
                WheelVars.CurrentRadioWheel.SelectedCategory = pair.Category;
                RadioStation.NextQueuedStation = pair.Station;
            }
        }

        public static StationWheelPair GetVehicleStationInfo(Vehicle vehicle) {
            return IsUsedVehicle(vehicle) ? GetFromList(vehicle).RadioInfo : null;
        }

        internal static void Reset() {
            // StationWheelPair instances are rebuilt on every catalog reload. Keeping them
            // here would retain disposed stations/wheels and make restoration fail because
            // the new catalog contains different pair objects.
            vehicles.Clear();
        }
    }

    internal sealed class UsedVehicle {
        internal int Handle { get; }
        internal StationWheelPair RadioInfo { get; set; }

        public UsedVehicle(Vehicle vehicle, StationWheelPair pair) {
            Handle = vehicle.Handle;
            RadioInfo = pair;
        }
    }
}
