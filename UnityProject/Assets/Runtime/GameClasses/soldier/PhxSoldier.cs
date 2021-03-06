using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using LibSWBF2.Utils;
using System.Runtime.ExceptionServices;

public class PhxSoldier : PhxControlableInstance<PhxSoldier.ClassProperties>
{
    static PhxGameRuntime GAME => PhxGameRuntime.Instance;
    static PhxRuntimeMatch MTC => PhxGameRuntime.GetMatch();
    static PhxRuntimeScene SCENE => PhxGameRuntime.GetScene();
    static PhxCamera CAM => PhxGameRuntime.GetCamera();


    public class ClassProperties : PhxClass
    {
        public PhxProp<Texture2D> MapTexture = new PhxProp<Texture2D>(null);
        public PhxProp<float> MapScale = new PhxProp<float>(1.0f);
        public PhxProp<float> MapViewMin = new PhxProp<float>(1.0f);
        public PhxProp<float> MapViewMax = new PhxProp<float>(1.0f);
        public PhxProp<float> MapSpeedMin = new PhxProp<float>(1.0f);
        public PhxProp<float> MapSpeedMax = new PhxProp<float>(1.0f);

        public PhxProp<string> HealthType = new PhxProp<string>("person");
        public PhxProp<float>  MaxHealth = new PhxProp<float>(100.0f);

        // Default animation for soldier classes seems to be hardcoded to "human".
        // For example, there's no "AnimationName" anywhere in the odf hierarchy:
        //   rep_inf_ep3_rifleman -> rep_inf_default_rifleman -> rep_inf_default -> com_inf_default
        public PhxProp<string> AnimationName = new PhxProp<string>("human");
        public PhxProp<string> SkeletonName = new PhxProp<string>("human");

        public PhxProp<float> MaxSpeed = new PhxProp<float>(1.0f);
        public PhxProp<float> MaxStrafeSpeed = new PhxProp<float>(1.0f);
        public PhxProp<float> MaxTurnSpeed = new PhxProp<float>(1.0f);
        public PhxProp<float> JumpHeight = new PhxProp<float>(1.0f);
        public PhxProp<float> JumpForwardSpeedFactor = new PhxProp<float>(1.0f);
        public PhxProp<float> JumpStrafeSpeedFactor = new PhxProp<float>(1.0f);
        public PhxProp<float> RollSpeedFactor = new PhxProp<float>(1.0f);
        public PhxProp<float> Acceleration = new PhxProp<float>(1.0f);
        public PhxProp<float> SprintAccelerateTime = new PhxProp<float>(1.0f);

        public PhxMultiProp ControlSpeed = new PhxMultiProp(typeof(string), typeof(float), typeof(float), typeof(float));

        public PhxProp<float> EnergyBar = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyRestore = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyRestoreIdle = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyDrainSprint = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyMinSprint = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyCostJump = new PhxProp<float>(0.0f);
        public PhxProp<float> EnergyCostRoll = new PhxProp<float>(1.0f);

        public PhxProp<float> AimValue = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorPostureSpecial = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorPostureStand = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorPostureCrouch = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorPostureProne = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorStrafe = new PhxProp<float>(0.0f);
        public PhxProp<float> AimFactorMove = new PhxProp<float>(1.0f);

        public PhxPropertySection Weapons = new PhxPropertySection(
            "WEAPONSECTION",
            ("WeaponName",    new PhxProp<string>(null)),
            ("WeaponAmmo",    new PhxProp<int>(0)),
            ("WeaponChannel", new PhxProp<int>(0))
        );

        public PhxProp<string> AISizeType = new PhxProp<string>("SOLDIER");
    }

    // Original SWBF2 Control States, see: com_inf_default.odf
    enum PhxControlState
    {
        Stand,
        Crouch,
        Prone,
        Sprint,
        Jet,
        Jump,
        Roll,
        Tumble
    }


    public PhxProp<float> CurHealth = new PhxProp<float>(100.0f);

    public PhxHumanAnimator Animator { get; private set; }
    Rigidbody Body;

    // Important skeleton bones
    Transform HpWeapons;
    Transform Spine;
    Transform Neck;

    PhxControlState State;

    // Physical raycast downwards
    bool Grounded;
    bool PrevGrounded;

    // how long to still be alerted after the last fire / hit
    const float AlertTime = 3f;
    float AlertTimer;

    // Time we have to fail the raycast to be considered falling
    //const float FallTime = 0.5f;
    //float FallTimer;

    // count time while not grounded
    float FallTimer;

    // minimum time we're considered falling when jumping
    const float JumpTime = 0.2f;
    float JumpTimer;

    // when > 0, we're currently landing
    float LandTimer;

    // time it takes to turn left/right when idle (not walking)
    const float TurnTime = 0.2f;
    float TurnTimer;
    Quaternion TurnStart;

    Vector3 CurrSpeed;


    bool bHasLookaroundIdleAnim = false;
    bool bHasCheckweaponIdleAnim = false;
    bool LastIdle = false;
    const float IdleTime = 10f;

    // <stance>, <thrustfactor> <strafefactor> <turnfactor>
    float[][] ControlValues;

    // First array index is whether:
    // - 0 : Primary Weapon
    // - 1 : Secondary Weapon
    IPhxWeapon[][] Weapons = new IPhxWeapon[2][];
    int[] WeaponIdx = new int[2] { -1, -1 };

    PhxInstance Aim;


    public override void Init()
    {
        ViewConstraint.x = 45f;

        // TODO: base turn speed in degreees/sec really 45?
        MaxTurnSpeed.y = 45f * C.MaxTurnSpeed;

        //CurrDir = transform.rotation;
        //TargetDir = CurrDir;

        PhxControlState[] states = (PhxControlState[])Enum.GetValues(typeof(PhxControlState));
        ControlValues = new float[states.Length][];
        for (int i = 0; i < states.Length; ++i)
        {
            ControlValues[i] = GetControlSpeed(states[i]);
        }

        HpWeapons = transform.Find("dummyroot/bone_root/bone_a_spine/bone_b_spine/bone_ribcage/bone_r_clavicle/bone_r_upperarm/bone_r_forearm/bone_r_hand/hp_weapons");
        Neck = transform.Find("dummyroot/bone_root/bone_a_spine/bone_b_spine/bone_ribcage/bone_neck");
        Spine = transform.Find("dummyroot/bone_root/bone_a_spine");
        Debug.Assert(HpWeapons != null);
        Debug.Assert(Neck != null);
        Debug.Assert(Spine != null);

        Body = gameObject.AddComponent<Rigidbody>();
        Body.mass = 80f;
        Body.drag = 0.2f;
        Body.angularDrag = 10f;
        Body.interpolation = RigidbodyInterpolation.Extrapolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        CapsuleCollider coll = gameObject.AddComponent<CapsuleCollider>();
        coll.height = 1.9f;
        coll.radius = 0.4f;
        coll.center = new Vector3(0f, 0.9f, 0f);


        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Weapons
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        var weapons = new List<IPhxWeapon>[2]
        {
            new List<IPhxWeapon>(),
            new List<IPhxWeapon>()
        };

        HashSet<string> weaponAnimBanks = new HashSet<string>();

        foreach (Dictionary<string, IPhxPropRef> section in C.Weapons)
        {
            int channel = 0;
            if (section.TryGetValue("WeaponChannel", out IPhxPropRef chVal))
            {
                PhxProp<int> weapCh = (PhxProp<int>)chVal;
                channel = weapCh;
            }
            Debug.Assert(channel >= 0 && channel < 2);

            if (section.TryGetValue("WeaponName", out IPhxPropRef nameVal))
            {
                PhxProp<string> weapCh = (PhxProp<string>)nameVal;
                PhxClass weapClass = SCENE.GetClass(weapCh);
                if (weapClass != null)
                {
                    PhxProp<int> medalProp = weapClass.P.Get<PhxProp<int>>("MedalsTypeToUnlock");
                    if (medalProp != null && medalProp != 0)
                    {
                        // Skip medal/award weapons for now
                        continue;
                    }

                    IPhxWeapon weap = SCENE.CreateInstance(weapClass, false, HpWeapons) as IPhxWeapon;
                    if (weap != null)
                    {
                        string weapAnimName = weap.GetAnimBankName();
                        if (!string.IsNullOrEmpty(weapAnimName))
                        {
                            weaponAnimBanks.Add(weapAnimName);
                        }

                        weapons[channel].Add(weap);

                        // init weapon as inactive
                        weap.GetInstance().gameObject.SetActive(false);
                        weap.OnShot(() => FireAnimation(channel == 0));
                        weap.OnReload(Reload);
                    }
                    else
                    {
                        Debug.LogWarning($"Instantiation of weapon class '{weapCh}' failed!");
                    }
                }
                else
                {
                    Debug.LogWarning($"Cannot find weapon class '{weapCh}'!");
                }
            }

            // TODO: weapon ammo
        }

        Weapons[0] = weapons[0].Count == 0 ? new IPhxWeapon[1] { null } : weapons[0].ToArray();
        Weapons[1] = weapons[1].Count == 0 ? new IPhxWeapon[1] { null } : weapons[1].ToArray();


        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Animation
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        Animator = gameObject.AddComponent<PhxHumanAnimator>();
        Animator.OnStateFinished += StateFinished;

        string[] weapAnimBanks = new string[weaponAnimBanks.Count];
        weaponAnimBanks.CopyTo(weapAnimBanks);
        Animator.Init(weapAnimBanks);


        // this needs to happen after the Animator is initialized, since swicthing
        // will weapons will most likely cause an animation bank change aswell
        NextWeapon(0);
        NextWeapon(1);
    }

    public override void BindEvents()
    {
        
    }

    public override void Fixate()
    {
        Destroy(Body);
        Body = null;

        Destroy(GetComponent<CapsuleCollider>());
    }

    public override IPhxWeapon GetPrimaryWeapon()
    {
        return Weapons[0][WeaponIdx[0]];
    }

    public void AddHealth(float amount)
    {
        if (amount < 0)
        {
            // we got hit! alert!
            AlertTimer = AlertTime;
        }

        float health = CurHealth + amount;
        if (health <= 0f)
        {
            health = 0;
            // TODO: dead!
        }
        CurHealth.Set(Mathf.Min(health, C.MaxHealth));
    }

    public void AddAmmo(float amount)
    {
        // TODO
    }

    public void NextWeapon(int channel)
    {
        Debug.Assert(channel >= 0 && channel < 2);

        if (WeaponIdx[channel] >= 0 && Weapons[channel][WeaponIdx[channel]] != null)
        {
            Weapons[channel][WeaponIdx[channel]].GetInstance().gameObject.SetActive(false);
        }
        if (++WeaponIdx[channel] >= Weapons[channel].Length)
        {
            WeaponIdx[channel] = 0;
        }
        if (Weapons[channel][WeaponIdx[channel]] != null)
        {
            Weapons[channel][WeaponIdx[channel]].GetInstance().gameObject.SetActive(true);
            Animator.SetAnimBank(Weapons[channel][WeaponIdx[channel]].GetAnimBankName());
        }
        else
        {
            Debug.LogWarning($"Encountered NULL weapon at channel {channel} and weapon index {WeaponIdx[channel]}!");
        }
    }

    public override void PlayIntroAnim()
    {
        Animator.PlayIntroAnim();
    }

    public override PhxInstance GetAim()
    {
        return Aim;
    }

    void FireAnimation(bool primary)
    {
        Animator.SetState(1, Animator.StandShootPrimary);
        Animator.RestartState(1);
    }

    void Reload()
    {
        IPhxWeapon weap = Weapons[0][WeaponIdx[0]];
        if (weap != null)
        {
            Animator.SetState(1, Animator.StandReload);
            Animator.RestartState(1);
            float animTime = Animator.GetCurrentState(1).GetDuration();
            Animator.SetPlaybackSpeed(1, Animator.StandReload, 1f / (weap.GetReloadTime() / animTime));
        }
    }

    // see: com_inf_default
    float[] GetControlSpeed(PhxControlState state)
    {
        foreach (object[] values in C.ControlSpeed.Values)
        {
            string controlName = values[0] as string;
            if (!string.IsNullOrEmpty(controlName) && controlName == state.ToString().ToLowerInvariant())
            {
                return new float[3]
                {
                    (float)values[1],
                    (float)values[2],
                    (float)values[3],
                };
            }
        }
        Debug.LogError($"Cannot find control state '{state}'!");
        return null;
    }

    void StateFinished(int layer)
    {
        if (layer == 1)
        {
            Animator.SetState(1, CraSettings.STATE_NONE);
        }
    }

    void Update()
    {
        UpdatePhysics(Time.deltaTime);
        UpdateState(Time.deltaTime);
    }

    void UpdateState(float deltaTime)
    {
        // Update Animator BEFORE firing any projectiles!
        Animator.Tick(deltaTime);
        AnimationCorrection();

        AlertTimer = Mathf.Max(AlertTimer - deltaTime, 0f);
        //Weap.Fire = false;

        if (Controller != null)
        {
            Vector3 aimStart = Neck.position;
            Vector3 aimDir = Controller.ViewDirection;
            if (Controller is PhxPlayerController)
            {
                aimStart = CAM.transform.position;
            }

            // ignore vehicle colliders
            int layerMask = 7;
            if (Physics.Raycast(aimStart, aimDir, out RaycastHit hit, 1000f, layerMask))
            {
                TargetPos = hit.point;
                Debug.DrawLine(aimStart, TargetPos, Color.blue);

                PhxInstance GetInstance(Transform t)
                {
                    PhxInstance inst = t.gameObject.GetComponent<PhxInstance>();
                    if (inst == null && t.parent != null)
                    {
                        return GetInstance(t.parent);
                    }
                    return inst;
                }
                Aim = GetInstance(hit.collider.gameObject.transform);
            }
            else
            {
                TargetPos = aimStart + aimDir * 1000f;
                Debug.DrawRay(aimStart, aimDir * 1000f, Color.blue);
            }

            if (Grounded && LandTimer <= 0f && TurnTimer <= 0f)
            {
                // Stand - Crouch - Sprint
                if (State == PhxControlState.Stand || State == PhxControlState.Crouch || State == PhxControlState.Sprint)
                {
                    // ---------------------------------------------------------------------------------------------
                    // Forward
                    // ---------------------------------------------------------------------------------------------
                    float walk = Mathf.Clamp01(Controller.MoveDirection.magnitude);
                    if (Controller.MoveDirection.y <= 0f)
                    {
                        // invert animation direction for strafing (left/right)
                        walk = -walk;
                    }

                    if (State == PhxControlState.Sprint)
                    {
                        Animator.SetState(0, Animator.StandSprint);
                    }
                    else if (walk > 0.2f && walk <= 0.75f)
                    {
                        Animator.SetState(0, AlertTimer > 0f ? Animator.StandAlertWalk : Animator.StandWalk);
                        Animator.SetPlaybackSpeed(0, Animator.StandWalk, walk / 0.75f);
                    }
                    else if (walk > 0.75f)
                    {
                        Animator.SetState(0, AlertTimer > 0f ? Animator.StandAlertRun : Animator.StandRun);
                        Animator.SetPlaybackSpeed(0, Animator.StandRun, walk / 1f);
                    }
                    else if (walk < -0.2f)
                    {
                        Animator.SetState(0, AlertTimer > 0f ? Animator.StandAlertBackward : Animator.StandBackward);
                        Animator.SetPlaybackSpeed(0, Animator.StandBackward, -walk / 1f);
                    }
                    else
                    {
                        Animator.SetState(0, AlertTimer > 0f ? Animator.StandAlertIdle : Animator.StandIdle);
                    }
                    // ---------------------------------------------------------------------------------------------
                }

                // Stand - Crouch
                if (State == PhxControlState.Stand || State == PhxControlState.Crouch)
                {
                    // ---------------------------------------------------------------------------------------------
                    // Idle
                    // ---------------------------------------------------------------------------------------------
                    if (Controller.IdleTime >= IdleTime)
                    {
                        if (bHasLookaroundIdleAnim && !bHasCheckweaponIdleAnim)
                        {
                            //Anim.SetTrigger(IdleNames[0]);
                        }
                        else if (!bHasLookaroundIdleAnim && bHasCheckweaponIdleAnim)
                        {
                            //Anim.SetTrigger(IdleNames[1]);
                        }
                        else if (bHasLookaroundIdleAnim && bHasCheckweaponIdleAnim)
                        {
                            //Anim.SetTrigger(IdleNames[UnityEngine.Random.Range(0, 1)]);
                        }
                        Controller.ResetIdleTime();
                    }
                    if (!Controller.IsIdle && LastIdle)
                    {
                        //Anim.SetTrigger("UnIdle");
                    }
                    // ---------------------------------------------------------------------------------------------


                    // ---------------------------------------------------------------------------------------------
                    // Shooting
                    // ---------------------------------------------------------------------------------------------
                    if (Weapons[0][WeaponIdx[0]].GetReloadProgress() == 1f)
                    {
                        if (Controller.ShootPrimary)
                        {
                            // only fire when not currently turning
                            //Weap.Fire = TurnTimer <= 0f;

                            Weapons[0][WeaponIdx[0]].Fire(Controller, TargetPos);
                            AlertTimer = AlertTime;
                        }
                        else if (Controller.ShootSecondary)
                        {
                            Weapons[1][WeaponIdx[1]].Fire(Controller, TargetPos);
                            AlertTimer = AlertTime;
                        }
                        else if (Controller.Reload)
                        {
                            Weapons[0][WeaponIdx[0]].Reload();
                            //Anim.SetTrigger("Reload");
                        }
                    }
                    // ---------------------------------------------------------------------------------------------
                    if (Controller.NextPrimaryWeapon)
                    {
                        NextWeapon(0);
                    }
                    if (Controller.NextSecondaryWeapon)
                    {
                        NextWeapon(1);
                    }


                    State = Controller.Crouch ? PhxControlState.Crouch : PhxControlState.Stand;
                }

                // Stand - Sprint
                if (State == PhxControlState.Stand || State == PhxControlState.Sprint)
                {
                    // ---------------------------------------------------------------------------------------------
                    // Jumping
                    // ---------------------------------------------------------------------------------------------
                    if (Controller.Jump)
                    {
                        Body?.AddForce(Vector3.up * Mathf.Sqrt(C.JumpHeight * -2f * Physics.gravity.y) + CurrSpeed, ForceMode.VelocityChange);
                        State = PhxControlState.Jump;
                        JumpTimer = JumpTime;

                        Animator.SetState(0, Animator.Jump);
                        Animator.SetState(1, CraSettings.STATE_NONE);
                    }
                    // ---------------------------------------------------------------------------------------------
                }

                // Stand
                if (State == PhxControlState.Stand)
                {
                    if (Controller.MoveDirection.y > 0.2f && Controller.Sprint && Weapons[0][WeaponIdx[0]].GetReloadProgress() == 1f)
                    {
                        State = PhxControlState.Sprint;
                    }
                }

                // Crouch
                if (State == PhxControlState.Crouch)
                {
                    if (Controller.Jump)
                    {
                        State = PhxControlState.Stand;
                    }

                    // TODO: verify
                    else if (Controller.MoveDirection.y > 0.8f && Controller.Sprint)
                    {
                        State = PhxControlState.Sprint;
                    }
                }

                // Sprint
                if (State == PhxControlState.Sprint)
                {
                    if (Controller.MoveDirection.y < 0.8f || !Controller.Sprint)
                    {
                        State = PhxControlState.Stand;
                    }
                }

                // Jump
                if (State == PhxControlState.Jump)
                {
                    JumpTimer -= deltaTime;
                    if (Grounded && JumpTimer < 0f)
                    {
                        State = PhxControlState.Stand;
                    }
                }

                //Anim.SetBool("Alert", AlertTimer > 0f);
                LastIdle = Controller.IsIdle;

                FallTimer = 0f;
            }
        }
    }

    void UpdatePhysics(float deltaTime)
    {
        Grounded = Physics.CheckSphere(transform.position, 0.4f, PhxGameRuntime.PlayerMask, QueryTriggerInteraction.Ignore);

        if (Controller != null)
        {
            if (!PrevGrounded && Grounded)
            {
                if (FallTimer > 1.5f)
                {
                    LandTimer = 0.9f;
                    Animator.SetState(0, Animator.LandHard);
                    Animator.SetState(1, CraSettings.STATE_NONE);
                    //Debug.Log($"Land HARD {FallTimer}");
                }
                else if (FallTimer > 1.1f || Controller.MoveDirection.magnitude < 0.1f)
                {
                    LandTimer = 0.6f;
                    Animator.SetState(0, Animator.LandSoft);
                    Animator.SetState(1, CraSettings.STATE_NONE);
                    //Debug.Log($"Land Soft {FallTimer}");
                }
                else
                {
                    LandTimer = 0.05f;
                    Animator.SetState(0, Animator.LandSoft);
                    Animator.SetState(1, CraSettings.STATE_NONE);
                    //Debug.Log($"Land very Soft {FallTimer}");
                }
            }
            else
            {
                LandTimer = Mathf.Max(LandTimer - deltaTime, 0f);
            }
            PrevGrounded = Grounded;

            if (LandTimer <= 0f)
            {
                Vector3 lookWalkForward = Controller.ViewDirection;
                lookWalkForward.y = 0f;
                Quaternion lookRot = Quaternion.LookRotation(lookWalkForward);

                float accStep      = C.Acceleration * deltaTime;
                float thrustFactor = ControlValues[(int)State][0];
                float strafeFactor = ControlValues[(int)State][1];
                float turnFactor   = ControlValues[(int)State][2];

                Vector3 moveDirLocal = new Vector3(Controller.MoveDirection.x * turnFactor, 0f, Controller.MoveDirection.y);
                Vector3 moveDirWorld = lookRot * moveDirLocal;

                // TODO: base turn speed in degreees/sec really 45?
                MaxTurnSpeed.y = 45f * C.MaxTurnSpeed * turnFactor;

                if (moveDirLocal.magnitude == 0f)
                {
                    CurrSpeed *= 0.1f * deltaTime;

                    if (TurnTimer > 0f)
                    {
                        lookRot = Quaternion.Slerp(lookRot, TurnStart, TurnTimer / TurnTime);
                    }
                    else
                    {
                        //float rotDiff = Quaternion.Angle(transform.rotation, lookRot);
                        float rotDiff = Mathf.DeltaAngle(transform.rotation.eulerAngles.y, lookRot.eulerAngles.y);
                        if (rotDiff < -40f || rotDiff > 60f)
                        {
                            TurnTimer = TurnTime;
                            TurnStart = transform.rotation;
                            Animator.SetState(0, rotDiff < 0f ? Animator.TurnLeft : Animator.TurnRight);
                            Animator.SetState(1, CraSettings.STATE_NONE);
                            Animator.RestartState(0);
                        }

                        lookRot = transform.rotation;
                    }
                }
                else
                {
                    CurrSpeed += moveDirWorld * accStep;

                    float maxSpeed = moveDirLocal.z < 0.2f ? C.MaxStrafeSpeed : C.MaxSpeed;
                    float forwardFactor = moveDirLocal.z < 0.2f ? strafeFactor : thrustFactor;
                    CurrSpeed = Vector3.ClampMagnitude(CurrSpeed, maxSpeed * forwardFactor);

                    if (moveDirLocal.z <= 0f)
                    {
                        // invert look direction when strafing left/right
                        moveDirWorld = -moveDirWorld;
                    }

                    lookRot = Quaternion.LookRotation(moveDirWorld);
                }

                if (!Grounded)
                {
                    FallTimer += deltaTime;
                    if (FallTimer > 0.1f && State != PhxControlState.Jump)
                    {
                        Animator.SetState(0, Animator.Fall);
                        Animator.SetState(1, CraSettings.STATE_NONE);
                        State = PhxControlState.Jump;
                        Body?.AddForce(CurrSpeed, ForceMode.VelocityChange);
                    }
                }
                else
                {
                    Body?.MovePosition(Body.position + CurrSpeed * deltaTime);
                }

                Body?.MoveRotation(lookRot);
            }

            TurnTimer = Mathf.Max(TurnTimer - deltaTime, 0f);
        }
    }

    public Vector3 RotAlt1 = new Vector3(7f, -78f, -130f);
    public Vector3 RotAlt2 = new Vector3(0f, -50f, -75f);
    public Vector3 RotAlt3 = new Vector3(0f, -68f, -81f);
    public Vector3 RotAlt4 = new Vector3(0f, -53f, -77f);

    void AnimationCorrection()
    {
        if (Controller == null) return;

        if (State == PhxControlState.Stand || State == PhxControlState.Crouch)
        {
            if (Animator.GetCurrentStateIdx(1) == Animator.StandShootPrimary)
            {
                Spine.rotation = Quaternion.LookRotation(Controller.ViewDirection) * Quaternion.Euler(RotAlt4);
            }
            else if (AlertTimer > 0f)
            {
                if (Controller.MoveDirection.magnitude > 0.1f)
                {
                    Spine.rotation = Quaternion.LookRotation(Controller.ViewDirection) * Quaternion.Euler(RotAlt3);
                }
                else
                {
                    Spine.rotation = Quaternion.LookRotation(Controller.ViewDirection) * Quaternion.Euler(RotAlt2);
                }
            }
            else
            {
                Neck.rotation = Quaternion.LookRotation(Controller.ViewDirection) * Quaternion.Euler(RotAlt1);
            }
        }
    }
}
