using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Contrôleur 2D du joueur (Rigidbody2D + BoxCollider2D).
/// - Avance automatiquement vers la droite (autoRun) ou se déplace aux flèches.
/// - Flèche Haut : saut (gravité asymétrique + coyote time + jump buffer).
/// - Flèche Bas : glissade (réduit temporairement le collider).
/// - Flèches Gauche/Droite : ajustement horizontal.
/// - isInverted : inverse les directions (utilisé par TwistManager).
/// 
/// Unity 6 : on utilise rb.linearVelocity.
/// Unity 2022 LTS : remplacer "linearVelocity" par "velocity".
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Course")]
    [Tooltip("Si activé, le personnage avance seul vers la droite.")]
    [SerializeField] private bool autoRun = true;
    [SerializeField] private float runSpeed = 6f;
    [Tooltip("Vitesse de l'ajustement gauche/droite quand autoRun est activé (garde-la < runSpeed).")]
    [SerializeField] private float horizontalAdjustSpeed = 3f;

    [Header("Saut (game feel)")]
    [Tooltip("Hauteur du saut en unités (mètres).")]
    [SerializeField] private float jumpHeight = 2.5f;
    [Tooltip("Gravité multipliée à la descente : saut plus 'pesant'.")]
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [Tooltip("Gravité multipliée si on relâche la touche en montée : saut court.")]
    [SerializeField] private float lowJumpGravityMultiplier = 2f;
    [SerializeField] private float maxFallSpeed = 20f;
    [Tooltip("Délai (s) pendant lequel on peut encore sauter après avoir quitté le sol.")]
    [SerializeField] private float coyoteTime = 0.1f;
    [Tooltip("Délai (s) pendant lequel un appui trop tôt est mémorisé.")]
    [SerializeField] private float jumpBufferTime = 0.1f;

    [Header("Glissade")]
    [SerializeField] private float slideDuration = 0.6f;
    [Range(0.2f, 0.9f)]
    [Tooltip("Fraction de la hauteur du collider conservée pendant la glissade.")]
    [SerializeField] private float slideHeightFactor = 0.5f;

    [Header("Sol")]
    [Tooltip("Layer(s) considéré(s) comme sol. Doit être renseigné, sinon le saut ne marche pas !")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Twist")]
    [Tooltip("Si vrai, les directions gauche/droite sont multipliées par -1.")]
    public bool isInverted;
    [Tooltip("Si vrai, l'inversion échange aussi Saut (Haut) et Glissade (Bas).")]
    [SerializeField] private bool invertJumpAndSlideToo = false;

    // --- Etat public en lecture seule (utile pour animations / debug) ---
    public bool IsGrounded { get; private set; }
    public bool IsSliding { get; private set; }

    // --- Composants ---
    private Rigidbody2D rb;
    private BoxCollider2D col;
    private float baseGravityScale;
    private Vector2 originalColSize;
    private Vector2 originalColOffset;

    // --- Input System (actions créées en code : aucun asset à configurer) ---
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction slideAction;

    // --- Etat interne ---
    private float moveInput;
    private bool jumpHeld;
    private float coyoteCounter;
    private float jumpBufferCounter;
    private float slideTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<BoxCollider2D>();
        baseGravityScale = rb.gravityScale;
        originalColSize = col.size;
        originalColOffset = col.offset;
        BuildInputActions();
    }

    private void Start()
    {
        if (groundLayer.value == 0)
            Debug.LogWarning("[PlayerController] 'Ground Layer' est vide : le joueur ne détectera jamais le sol.", this);
    }

    private void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        slideAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
        slideAction.Disable();
    }

    private void OnDestroy()
    {
        moveAction?.Dispose();
        jumpAction?.Dispose();
        slideAction?.Dispose();
    }

    private void BuildInputActions()
    {
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/leftArrow")
            .With("Positive", "<Keyboard>/rightArrow");

        jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/upArrow");
        slideAction = new InputAction("Slide", InputActionType.Button, "<Keyboard>/downArrow");
    }

    private void Update()
    {
        ReadInput(out bool jumpPressed, out bool slidePressed);

        IsGrounded = CheckGround();

        // Timers "de ressenti" en temps réel : indépendants du ralenti (timeScale).
        coyoteCounter = IsGrounded ? coyoteTime : coyoteCounter - Time.unscaledDeltaTime;
        jumpBufferCounter = jumpPressed ? jumpBufferTime : jumpBufferCounter - Time.unscaledDeltaTime;

        // Saut
        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
            TryJump();

        // Glissade
        if (slidePressed && IsGrounded && !IsSliding)
            StartSlide();

        if (IsSliding)
        {
            slideTimer -= Time.deltaTime;
            if (slideTimer <= 0f && !CeilingAbove())
                EndSlide();
        }
    }

    private void FixedUpdate()
    {
        // Mouvement horizontal
        float vx = autoRun
            ? runSpeed + moveInput * horizontalAdjustSpeed
            : moveInput * runSpeed;

        // Gravité ajustée pour un bon game feel
        float vy = rb.linearVelocity.y;
        if (vy < -0.01f)
            rb.gravityScale = baseGravityScale * fallGravityMultiplier;
        else if (vy > 0.01f && !jumpHeld)
            rb.gravityScale = baseGravityScale * lowJumpGravityMultiplier;
        else
            rb.gravityScale = baseGravityScale;

        vy = Mathf.Max(vy, -maxFallSpeed);
        rb.linearVelocity = new Vector2(vx, vy);
    }

    // ------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------
    private void ReadInput(out bool jumpPressed, out bool slidePressed)
    {
        float dir = moveAction.ReadValue<float>();
        jumpPressed = jumpAction.WasPressedThisFrame();
        slidePressed = slideAction.WasPressedThisFrame();
        jumpHeld = jumpAction.IsPressed();

        if (isInverted)
        {
            dir = -dir; // inversion des directions

            if (invertJumpAndSlideToo)
            {
                (jumpPressed, slidePressed) = (slidePressed, jumpPressed);
                jumpHeld = slideAction.IsPressed();
            }
        }

        moveInput = dir;
    }

    // ------------------------------------------------------------------
    // Saut
    // ------------------------------------------------------------------
    private void TryJump()
    {
        // Impossible de sauter si on est coincé sous un obstacle en glissade.
        if (IsSliding)
        {
            if (CeilingAbove()) return;
            EndSlide();
        }

        // v = sqrt(2 * g * h) avec la gravité de base du Rigidbody2D
        float g = Mathf.Abs(Physics2D.gravity.y) * baseGravityScale;
        float jumpVelocity = Mathf.Sqrt(2f * g * jumpHeight);

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVelocity);
        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
    }

    // ------------------------------------------------------------------
    // Glissade : on réduit le collider en gardant les pieds au même endroit
    // ------------------------------------------------------------------
    private void StartSlide()
    {
        IsSliding = true;
        slideTimer = slideDuration;

        float newHeight = originalColSize.y * slideHeightFactor;
        col.size = new Vector2(originalColSize.x, newHeight);
        col.offset = new Vector2(originalColOffset.x,
            originalColOffset.y - (originalColSize.y - newHeight) * 0.5f);
    }

    private void EndSlide()
    {
        IsSliding = false;
        col.size = originalColSize;
        col.offset = originalColOffset;
    }

    // ------------------------------------------------------------------
    // Détections (Physics2D.OverlapBox, sans objet "GroundCheck" à créer)
    // ------------------------------------------------------------------
    private bool CheckGround()
    {
        Bounds b = col.bounds;
        Vector2 center = new Vector2(b.center.x, b.min.y - 0.05f);
        Vector2 size = new Vector2(b.size.x * 0.9f, 0.1f);
        return Physics2D.OverlapBox(center, size, 0f, groundLayer) != null;
    }

    /// <summary>Vrai s'il y a un obstacle à l'endroit où le collider se redéploierait.</summary>
    private bool CeilingAbove()
    {
        // Suppose une échelle (1,1,1) sur le joueur.
        float extra = (originalColSize.y - col.size.y) * transform.lossyScale.y;
        if (extra <= 0.001f) return false;

        Bounds b = col.bounds;
        Vector2 center = new Vector2(b.center.x, b.max.y + extra * 0.5f);
        Vector2 size = new Vector2(b.size.x * 0.9f, extra * 0.9f);
        return Physics2D.OverlapBox(center, size, 0f, groundLayer) != null;
    }
}
