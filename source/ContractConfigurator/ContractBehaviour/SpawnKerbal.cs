﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;
using Contracts;
using ContractConfigurator;
using ContractConfigurator.ExpressionParser;

namespace ContractConfigurator.Behaviour
{
    /// <summary>
    /// Class for spawning a Kerbal.
    /// </summary>
    public class SpawnKerbal : ContractBehaviour, IHasKerbalBehaviour
    {
        private class KerbalData
        {
            public string name = null;
            public ProtoCrewMember crewMember = null;
            public CelestialBody body = null;
            public Orbit orbit = null;
            public double latitude = 0.0;
            public double longitude = 0.0;
            public double? altitude = 0.0;
            public bool landed = false;
            public bool owned = false;
            public bool addToRoster = true;
            public ProtoCrewMember.Gender? gender = null;
            public ProtoCrewMember.KerbalType kerbalType;
            public PQSCity pqsCity = null;
            public Vector3d pqsOffset;
            public float heading;

            public KerbalData() { }
            public KerbalData(KerbalData k)
            {
                name = k.name;
                crewMember = k.crewMember;
                body = k.body;
                orbit = k.orbit;
                latitude = k.latitude;
                longitude = k.longitude;
                altitude = k.altitude;
                landed = k.landed;
                owned = k.owned;
                addToRoster = k.addToRoster;
                gender = k.gender;
                kerbalType = k.kerbalType;
                pqsCity = k.pqsCity;
                pqsOffset = k.pqsOffset;
                heading = k.heading;
            }
        }
        private List<KerbalData> kerbals = new List<KerbalData>();
        private bool initialized = false;

        public int KerbalCount { get { return kerbals.Count; } }

        public SpawnKerbal() {}

        /// <summary>
        /// Copy constructor.
        /// </summary>
        /// <param name="orig"></param>
        public SpawnKerbal(SpawnKerbal orig)
        {
            foreach (KerbalData kerbal in orig.kerbals)
            {
                kerbals.Add(new KerbalData(kerbal));
            }

            if (orig.initialized)
            {
                foreach (KerbalData kerbal in orig.kerbals)
                {
                    kerbal.crewMember = null;
                }
                orig.initialized = false;
            }
            else
            {
                Initialize();
            }
        }

        public void Initialize()
        {
            if (!initialized)
            {
                LoggingUtil.LogVerbose(this, "Initializing SpawnKerbal.");

                // Create the CrewMember record
                foreach (KerbalData kerbal in kerbals)
                {
                    kerbal.crewMember = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(kerbal.kerbalType);
                    if (kerbal.gender != null)
                    {
                        kerbal.crewMember.gender = kerbal.gender.Value;
                    }

                    // Set the name
                    if (kerbal.name != null)
                    {
                        kerbal.crewMember.name = kerbal.name;
                    }

                    LoggingUtil.LogVerbose(this, "Generating kerbal " + kerbal.crewMember.name);

                    // Generate PQS city coordinates
                    if (kerbal.pqsCity != null)
                    {
                        LoggingUtil.LogVerbose(this, "Generating coordinates from PQS city for Kerbal " + kerbal.crewMember.name);

                        // Translate by the PQS offset (inverse transform of coordinate system)
                        Vector3d position = kerbal.pqsCity.transform.position;
                        Vector3d v = kerbal.pqsOffset;
                        Vector3d i = kerbal.pqsCity.transform.right;
                        Vector3d j = kerbal.pqsCity.transform.forward;
                        Vector3d k = kerbal.pqsCity.transform.up;
                        Vector3d offsetPos = new Vector3d(
                            (j.y * k.z - j.z * k.y) * v.x + (i.z * k.y - i.y * k.z) * v.y + (i.y * j.z - i.z * j.y) * v.z,
                            (j.z * k.x - j.x * k.z) * v.x + (i.x * k.z - i.z * k.x) * v.y + (i.z * j.x - i.x * j.z) * v.z,
                            (j.x * k.y - j.y * k.x) * v.x + (i.y * k.x - i.x * k.y) * v.y + (i.x * j.y - i.y * j.x) * v.z
                        );
                        offsetPos *= (i.x * j.y * k.z) + (i.y * j.z * k.x) + (i.z * j.x * k.y) - (i.z * j.y * k.x) - (i.y * j.x * k.z) - (i.x * j.z * k.y);
                        kerbal.latitude = kerbal.body.GetLatitude(position + offsetPos);
                        kerbal.longitude = kerbal.body.GetLongitude(position + offsetPos);
                    }
                }

                initialized = true;
                LoggingUtil.LogVerbose(this, "SpawnKerbal initialized.");
            }
        }

        public void Uninitialize()
        {
            if (initialized)
            {
                RemoveKerbals();
                initialized = false;
            }
        }
        
        public static SpawnKerbal Create(ConfigNode configNode, SpawnKerbalFactory factory)
        {
            SpawnKerbal spawnKerbal = new SpawnKerbal();

            bool valid = true;
            int index = 0;
            foreach (ConfigNode child in ConfigNodeUtil.GetChildNodes(configNode, "KERBAL"))
            {
                DataNode dataNode = new DataNode("KERBAL_" + index++, factory.dataNode, factory);
                try
                {
                    ConfigNodeUtil.SetCurrentDataNode(dataNode);

                    KerbalData kerbal = new KerbalData();

                    // Get name
                    valid &= ConfigNodeUtil.ParseValue<string>(child, "name", x => { kerbal.name = x; if (kerbal.crewMember != null) kerbal.crewMember.name = x; },
                        factory, (string)null);

                    // Use an expression to default - then it'll work for dynamic contracts
                    if (!child.HasValue("targetBody"))
                    {
                        child.AddValue("targetBody", "@/targetBody");
                    }
                    valid &= ConfigNodeUtil.ParseValue<CelestialBody>(child, "targetBody", x => kerbal.body = x, factory);

                    // Get landed stuff
                    if (child.HasValue("lat") && child.HasValue("lon") || child.HasValue("pqsCity"))
                    {
                        kerbal.landed = true;
                        if (child.HasValue("pqsCity"))
                        {
                            string pqsCityStr = null;
                            valid &= ConfigNodeUtil.ParseValue<string>(child, "pqsCity", x => pqsCityStr = x, factory);
                            if (pqsCityStr != null)
                            {
                                try
                                {
                                    kerbal.pqsCity = kerbal.body.GetComponentsInChildren<PQSCity>(true).Where(pqs => pqs.name == pqsCityStr).First();
                                }
                                catch (Exception e)
                                {
                                    LoggingUtil.LogError(typeof(WaypointGenerator), "Couldn't load PQSCity with name '" + pqsCityStr + "'");
                                    LoggingUtil.LogException(e);
                                    valid = false;
                                }
                            }
                            valid &= ConfigNodeUtil.ParseValue<Vector3d>(child, "pqsOffset", x => kerbal.pqsOffset = x, factory, new Vector3d());

                            // Don't expect these to load anything, but do it to mark as initialized
                            valid &= ConfigNodeUtil.ParseValue<double>(child, "lat", x => kerbal.latitude = x, factory, 0.0);
                            valid &= ConfigNodeUtil.ParseValue<double>(child, "lon", x => kerbal.longitude = x, factory, 0.0);
                        }
                        else
                        {
                            valid &= ConfigNodeUtil.ParseValue<double>(child, "lat", x => kerbal.latitude = x, factory);
                            valid &= ConfigNodeUtil.ParseValue<double>(child, "lon", x => kerbal.longitude = x, factory);
                        }

                        valid &= ConfigNodeUtil.ParseValue<float>(child, "heading", x => kerbal.heading = x, factory, 0.0f);
                    }
                    // Get orbit
                    else if (child.HasNode("ORBIT"))
                    {
                        // Don't expect these to load anything, but do it to mark as initialized
                        valid &= ConfigNodeUtil.ParseValue<double>(child, "lat", x => kerbal.latitude = x, factory, 0.0);
                        valid &= ConfigNodeUtil.ParseValue<double>(child, "lon", x => kerbal.longitude = x, factory, 0.0);

                        valid &= ConfigNodeUtil.ParseValue<Orbit>(child, "ORBIT", x => kerbal.orbit = x, factory);
                    }
                    else
                    {
                        // Will error
                        valid &= ConfigNodeUtil.ValidateMandatoryChild(child, "ORBIT", factory);
                    }

                    valid &= ConfigNodeUtil.ParseValue<double?>(child, "alt", x => kerbal.altitude = x, factory, (double?)null);

                    // Get additional stuff
                    valid &= ConfigNodeUtil.ParseValue<bool>(child, "owned", x => kerbal.owned = x, factory, false);
                    valid &= ConfigNodeUtil.ParseValue<bool>(child, "addToRoster", x => kerbal.addToRoster = x, factory, true);
                    valid &= ConfigNodeUtil.ParseValue<ProtoCrewMember.Gender?>(child, "gender", x => kerbal.gender = x, factory, (ProtoCrewMember.Gender?)null);
                    valid &= ConfigNodeUtil.ParseValue<ProtoCrewMember.KerbalType>(child, "kerbalType", x => kerbal.kerbalType = x, factory, ProtoCrewMember.KerbalType.Unowned);

                    // Check for unexpected values
                    valid &= ConfigNodeUtil.ValidateUnexpectedValues(child, factory);

                    // Add to the list
                    spawnKerbal.kerbals.Add(kerbal);
                }
                finally
                {
                    ConfigNodeUtil.SetCurrentDataNode(factory.dataNode);
                }
            }

            return valid ? spawnKerbal : null;
        }

        protected override void OnOffered()
        {
        }

        protected override void OnAccepted()
        {
            // Actually spawn the kerbals in the game world!
            foreach (KerbalData kerbal in kerbals)
            {
                LoggingUtil.LogVerbose(this, "Spawning a Kerbal named " + kerbal.crewMember.name);

                if (kerbal.altitude == null)
                {
                    kerbal.altitude = LocationUtil.TerrainHeight(kerbal.latitude, kerbal.longitude, kerbal.body);
                }

                // Set additional info for landed kerbals
                if (kerbal.landed)
                {
                    Vector3d pos = kerbal.body.GetWorldSurfacePosition(kerbal.latitude, kerbal.longitude, kerbal.altitude.Value);

                    kerbal.orbit = new Orbit(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, kerbal.body);
                    kerbal.orbit.UpdateFromStateVectors(pos, kerbal.body.getRFrmVel(pos), kerbal.body, Planetarium.GetUniversalTime());
                    LoggingUtil.LogVerbose(typeof(SpawnKerbal), "kerbal generated, orbit = " + kerbal.orbit);
                }
                else
                {
                    // Update the reference body in the orbit
                    kerbal.orbit.referenceBody = kerbal.body;
                }

                uint flightId = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);

                // Create crew member array
                ProtoCrewMember[] crewArray = new ProtoCrewMember[1];
                crewArray[0] = kerbal.crewMember;

                // Create part nodes
                ConfigNode[] partNodes = new ConfigNode[1];
                partNodes[0] = ProtoVessel.CreatePartNode(kerbal.crewMember.gender == ProtoCrewMember.Gender.Male ? "kerbalEVA" : "kerbalEVAfemale",
                    flightId, crewArray);

                // Create additional nodes
                ConfigNode[] additionalNodes = new ConfigNode[1];
                DiscoveryLevels discoveryLevel = kerbal.owned ? DiscoveryLevels.Owned : DiscoveryLevels.Unowned;
                additionalNodes[0] = ProtoVessel.CreateDiscoveryNode(discoveryLevel, UntrackedObjectClass.A, contract.TimeDeadline, contract.TimeDeadline);

                // Create the config node representation of the ProtoVessel
                ConfigNode protoVesselNode = ProtoVessel.CreateVesselNode(kerbal.crewMember.name, VesselType.EVA, kerbal.orbit, 0, partNodes, additionalNodes);

                // Additional seetings for a landed Kerbal
                if (kerbal.landed)
                {
                    bool splashed = kerbal.altitude.Value < 0.001;

                    // Add a bit of height for landed kerbals
                    if (!splashed)
                    {
                        kerbal.altitude += 0.2;
                    }

                    // Figure out the appropriate rotation
                    Vector3d norm = kerbal.body.GetRelSurfaceNVector(kerbal.latitude, kerbal.longitude);
                    Quaternion normal = Quaternion.LookRotation(new Vector3((float)norm.x, (float)norm.y, (float)norm.z));
                    Quaternion rotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
                    rotation = rotation * Quaternion.AngleAxis(kerbal.heading + 180, Vector3.up);

                    // Create the config node representation of the ProtoVessel
                    protoVesselNode.SetValue("sit", (splashed ? Vessel.Situations.SPLASHED : Vessel.Situations.LANDED).ToString());
                    protoVesselNode.SetValue("landed", (!splashed).ToString());
                    protoVesselNode.SetValue("splashed", splashed.ToString());
                    protoVesselNode.SetValue("lat", kerbal.latitude.ToString());
                    protoVesselNode.SetValue("lon", kerbal.longitude.ToString());
                    protoVesselNode.SetValue("alt", kerbal.altitude.ToString());
                    protoVesselNode.SetValue("rot", KSPUtil.WriteQuaternion(normal * rotation));

                    // Set the normal vector relative to the surface
                    Vector3 nrm = (rotation * Vector3.forward);
                    protoVesselNode.SetValue("nrm", nrm.x + "," + nrm.y + "," + nrm.z);
                }

                // Add vessel to the game
                HighLogic.CurrentGame.AddVessel(protoVesselNode);
            }
        }

        protected override void OnSave(ConfigNode configNode)
        {
            base.OnSave(configNode);

            foreach (KerbalData kd in kerbals)
            {
                ConfigNode child = new ConfigNode("KERBAL_DETAIL");

                child.AddValue("name", kd.crewMember.name);
                child.AddValue("body", kd.body.name);
                child.AddValue("lat", kd.latitude);
                child.AddValue("lon", kd.longitude);
                if (kd.altitude != null)
                {
                    child.AddValue("alt", kd.altitude);
                }
                child.AddValue("landed", kd.landed);
                child.AddValue("owned", kd.owned);
                child.AddValue("addToRoster", kd.addToRoster);

                if (kd.orbit != null)
                {
                    ConfigNode orbitNode = new ConfigNode("ORBIT");
                    new OrbitSnapshot(kd.orbit).Save(orbitNode);
                    child.AddNode(orbitNode);
                }

                configNode.AddNode(child);
            }
        }

        protected override void OnLoad(ConfigNode configNode)
        {
            base.OnLoad(configNode);

            foreach (ConfigNode child in configNode.GetNodes("KERBAL_DETAIL"))
            {
                // Read all the orbit data
                KerbalData kd = new KerbalData();
                kd.name = child.GetValue("name");
                kd.body = ConfigNodeUtil.ParseValue<CelestialBody>(child, "body");
                kd.latitude = ConfigNodeUtil.ParseValue<double>(child, "lat");
                kd.longitude = ConfigNodeUtil.ParseValue<double>(child, "lon");
                kd.altitude = ConfigNodeUtil.ParseValue<double?>(child, "alt", (double?)null);
                kd.landed = ConfigNodeUtil.ParseValue<bool>(child, "landed");
                kd.owned = ConfigNodeUtil.ParseValue<bool>(child, "owned");
                kd.addToRoster = ConfigNodeUtil.ParseValue<bool>(child, "addToRoster");

                if (child.HasNode("ORBIT"))
                {
                    kd.orbit = new OrbitSnapshot(child.GetNode("ORBIT")).Load();
                }

                // Find the ProtoCrewMember
                kd.crewMember = HighLogic.CurrentGame.CrewRoster.AllKerbals().Where(cm => cm != null && cm.name == kd.name).FirstOrDefault();

                // Add to the global list
                if (kd.crewMember != null)
                {
                    kerbals.Add(kd);
                }
            }
        }

        protected override void OnRegister()
        {
            GameEvents.onVesselRecovered.Add(new EventData<ProtoVessel>.OnEvent(OnVesselRecovered));
        }

        protected override void OnUnregister()
        {
            GameEvents.onVesselRecovered.Remove(new EventData<ProtoVessel>.OnEvent(OnVesselRecovered));
        }

        private void OnVesselRecovered(ProtoVessel v)
        {
            LoggingUtil.LogVerbose(this, "OnVesselRecovered: " + v);

            // EVA vessel
            if (v.vesselType == VesselType.EVA)
            {
                foreach (ProtoPartSnapshot p in v.protoPartSnapshots)
                {
                    foreach (string name in p.protoCrewNames)
                    {
                        // Find this crew member in our data
                        foreach (KerbalData kd in kerbals)
                        {
                            if (kd.name == name && kd.addToRoster)
                            {
                                // Add them to the roster
                                kd.crewMember.type = ProtoCrewMember.KerbalType.Crew;
                            }
                        }
                    }
                }

            }

            // Vessel with crew
            foreach (ProtoCrewMember crewMember in v.GetVesselCrew())
            {
                foreach (KerbalData kd in kerbals)
                {
                    if (kd.crewMember == crewMember && kd.addToRoster)
                    {
                        // Add them to the roster
                        crewMember.type = ProtoCrewMember.KerbalType.Crew;
                    }
                }
            }
        }

        protected override void OnCompleted()
        {
            RemoveKerbals(true);
        }

        protected override void OnCancelled()
        {
            RemoveKerbals();
        }

        protected override void OnDeadlineExpired()
        {
            RemoveKerbals();
        }

        protected override void OnDeclined()
        {
            RemoveKerbals();
        }

        protected override void OnGenerateFailed()
        {
            RemoveKerbals();
        }

        protected override void OnOfferExpired()
        {
            RemoveKerbals();
        }

        protected override void OnWithdrawn()
        {
            RemoveKerbals();
        }

        private void RemoveKerbals(bool onlyUnowned = false)
        {
            LoggingUtil.LogVerbose(this, "Removing kerbals, onlyUnowned = " + onlyUnowned);
            foreach (KerbalData kerbal in kerbals)
            {
                if (!kerbal.addToRoster || !onlyUnowned)
                {
                    LoggingUtil.LogVerbose(this, "    Removing " + kerbal.crewMember.name + "...");
                    // If it's an EVA make them disappear...
                    Vessel vessel = FlightGlobals.Vessels.Where(v => v.GetVesselCrew().Contains(kerbal.crewMember)).FirstOrDefault();
                    if (vessel != null && vessel.isEVA)
                    {
                        FlightGlobals.Vessels.Remove(vessel);
                    }
                }

                // Do not remove kerbals from the roster - as they may have done something of note
                // which puts them in the progress tracking logs.  If they are removed from the
                // roster, that will fail.
                //HighLogic.CurrentGame.CrewRoster.Remove(kerbal.crewMember.name);
                kerbal.crewMember = null;
            }
            kerbals.Clear();
        }

        public ProtoCrewMember GetKerbal(int index)
        {
            if (index < 0 || index >= kerbals.Count)
            {
                throw new Exception("ContractConfigurator: index " + index +
                    " is out of range for number of Kerbals spawned (" + kerbals.Count + ").");
            }

            return kerbals[index].crewMember;
        }

        public IEnumerable<ProtoCrewMember> Kerbals()
        {
            foreach (KerbalData kd in kerbals)
            {
                yield return kd.crewMember;
            }
        }
    }
}
