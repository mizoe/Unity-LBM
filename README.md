# Unity Lattice Boltzmann Method (LBM) Simulator

A lightweight, high-performance 2D/3D fluid simulation prototype implemented in Unity using Compute Shaders (HLSL). This project provides a real-time fluid dynamics environment based on the Lattice Boltzmann Method (LBM), designed as a foundational step toward an integrated aerodynamic evaluation tool.

## Video Demonstrations

Below are the real-time simulation results for both 2D and 3D solvers. You can play them directly inside GitHub.

### 2D Simulation (D2Q9)

[https://github.com/user-attachments/assets/41bd9e65-4089-4ce2-9615-ecd712490335](https://github.com/user-attachments/assets/41bd9e65-4089-4ce2-9615-ecd712490335)

### 3D Simulation (D3Q19)

[https://github.com/user-attachments/assets/56ee213d-de90-494f-a86e-92534bc4822f](https://github.com/user-attachments/assets/56ee213d-de90-494f-a86e-92534bc4822f)

### Car/Wagon Simulation (D3Q19)

[https://github.com/user-attachments/assets/108a95ae-5880-4165-b278-db784e424b51](https://github.com/user-attachments/assets/108a95ae-5880-4165-b278-db784e424b51)

---

## Getting Started

1. Open the project in Unity.
2. Open the **`SampleScene`** from the Project window.
3. In the Hierarchy window, you will find two main group GameObjects: **`2D`** and **`3D`**.
4. **Activate only one group** (either `2D` or `3D`) and deactivate the other.
5. Enter **Play Mode** to start the simulation.

### Interactive Controls (2D Mode)

* **Left Click / Drag:** Draw solid obstacles directly into the fluid domain in real-time. The fluid will dynamically recalculate and flow around the newly placed obstacles.

### Interactive Controls (3D Mode)

* **Slice Visualization:** You can move and rotate the Slice object (the contour plane) using the Unity Editor's Inspector or Scene View. The CFD contour data will update dynamically in real-time based on the Slice's current position and orientation.
* **Particle Emitter (SphereSeed):** Tracer particles are generated from the Emitter Sphere. You can adjust its size (scale) and position directly in the Inspector or Scene View to control the volume and origin of the streamlines.
* **Adding Obstacles:** Place your models as child objects under the `Obstacles` root GameObject and attach colliders to define their physical shape. **Only `BoxCollider`, `SphereCollider`, `CapsuleCollider`, and `MeshCollider` (with Convex ON) are supported.** Any other collider types, or objects/components that are set to Inactive, will be automatically ignored by the solver.

---

## File Structure

```text
Assets/
├── LBM2D/                          # 2D Fluid Simulation Environment (D2Q9)
│   ├── LbmController2D.cs          # Manages 2D buffers, inputs, and shader dispatch
│   ├── LbmSolver.compute           # D2Q9 LBM fluid solver core logic
│   ├── LbmVisualizer.shader        # Pixel shader for rendering 2D velocity/pressure
│   └── LbmVisualizer_Mat.mat       # Material applied to the 2D viewport Quad
│
├── LBM3D/                          # 3D Fluid Simulation Environment (D3Q19)
│   ├── LbmController3D.cs          # Manages 3D grid volumes, boundary conditions, and setup
│   ├── LbmSolver3D.compute         # D3Q19 LBM fluid solver core logic (3D Space)
│   ├── ParticleSolver.compute      # Compute shader for advecting 3D tracer particles
│   ├── SliceShader.shader          # Custom shader for scanning and visualizing contour sections
│   ├── LbmParticleVisualizer.shader# Custom shader optimized for rendering 3D fluid particles
│   ├── LbmParticle_Mat.mat         # Material optimized for 3D particle visualization
│   └── Slice_Mat.mat               # Material using SliceShader for contour visualization
│
├── Scenes/
│   └── SampleScene.unity           # Pre-configured scene with togglable 2D/3D simulator rigs
├── Documents/
│   ├── Lbm2D.mp4                   # Video demo of the 2D solver
│   ├── Lbm3D.mp4                   # Video demo of the 3D solver
│   └── Lbm3D_Wagon.mp4             # Video demo of the 3D Car/Wagon Simulation
└── README.md

```

## LBM Core Concepts & Models

The Lattice Boltzmann Method (LBM) simulates fluid dynamics by tracking the behavior of virtual particle groups on a discrete lattice, rather than directly solving the macroscopic Navier-Stokes equations.

Instead of dealing with continuous velocity fields, LBM tracks the **Particle Velocity Distribution Function** $f_i(\mathbf{x}, t)$, which represents the density of particles at position $\mathbf{x}$ and time $t$, moving with the discrete velocity vector $\mathbf{e}_i$.

The core governing equation is the discrete **Boltzmann Transport Equation** with the **BGK (Bhatnagar-Gross-Krook) collision approximation**:

$$f_i(\mathbf{x} + \mathbf{e}_i \Delta t, t + \Delta t) = f_i(\mathbf{x}, t) - \frac{1}{\tau} \left[ f_i(\mathbf{x}, t) - f_i^{eq}(\mathbf{x}, t) \right]$$

The algorithm executes in two distinct, highly-parallelizable phases every time step:

1. **Collision Phase:** Particles at each lattice site collide and relax toward their local equilibrium state $f_i^{eq}$ at a rate determined by the relaxation time $\tau$.
2. **Streaming Phase:** Particles move (stream) to their adjacent neighboring lattice sites along their respective velocity vectors $\mathbf{e}_i$.

---

### D2Q9 Model (2D Simulation)

The **D2Q9** model defines a 2-dimensional grid where particles can move in **9 discrete velocity directions** ($i = 0 \dots 8$): 1 stationary (center), 4 cardinal (up/down/left/right), and 4 diagonal directions.

```text
     [e6: Top-Left]     [e2: Top]     [e5: Top-Right]
                 ↖          ↑          ↗
     [e3: Left]   ←     [e0: Center]   →   [e1: Right]
                 ↙          ↓          ↘
     [e7: Bot-Left]     [e4: Bot]     [e8: Bot-Right]

```

#### Discrete Velocity Vectors ($\mathbf{e}_i$)

$$\begin{aligned}
\mathbf{e}_0 &= (0, 0) \\
\mathbf{e}_{1,2,3,4} &= \{ (1,0), (0,1), (-1,0), (0,-1) \} \\
\mathbf{e}_{5,6,7,8} &= \{ (1,1), (-1,1), (-1,-1), (1,-1) \}
\end{aligned}$$

#### Lattice Weights ($w_i$)

To ensure isotropic fluid behavior matching macro-scale physics, directional weights are mathematically fixed as:

* $w_0 = \frac{4}{9}$
* $w_{1 \dots 4} = \frac{1}{9}$
* $w_{5 \dots 8} = \frac{1}{36}$

---

### D3Q19 Model (3D Simulation)

For 3-dimensional space, this project utilizes the **D3Q19** (3D, 19-Velocity) model. It considers 1 stationary state, 6 face-aligned directions, and 12 edge-aligned directions.

*The 8 corner-aligned directions (vertices) are excluded.*

#### Why D3Q19 instead of D3Q27?

* **Memory Bandwidth Efficiency:** LBM on GPUs is heavily memory-bandwidth bound. Storing 19 floats ($76\text{ bytes}$) per cell instead of 27 floats ($108\text{ bytes}$) reduces GPU VRAM consumption and memory traffic by **approx. 30%**.
* **Aerodynamic Accuracy:** For isothermal, incompressible external aerodynamic flows (such as computing vehicle drag and wake vortices at highway speeds), D3Q19 provides an exceptionally accurate approximation of the Navier-Stokes equations, making the extra cost of D3Q27 unnecessary.

#### Discrete Velocity Vectors ($\mathbf{e}_i$) for D3Q19

$$\begin{aligned}
\mathbf{e}_0 &= (0, 0, 0) \\
\mathbf{e}_{1 \dots 6} &= \{ (\pm 1, 0, 0), (0, \pm 1, 0), (0, 0, \pm 1) \} & \text{(Face connections)} \\
\mathbf{e}_{7 \dots 18} &= \{ (\pm 1, \pm 1, 0), (\pm 1, 0, \pm 1), (0, \pm 1, \pm 1) \} & \text{(Edge connections)}
\end{aligned}$$

#### Lattice Weights ($w_i$) for D3Q19

* $w_0 = \frac{1}{3}$
* $w_{1 \dots 6} = \frac{1}{18}$
* $w_{7 \dots 18} = \frac{1}{36}$

---

### Macroscopic Quantities

At the end of each iteration, macroscopic fluid properties like **Density** ($\rho$) and **Velocity** ($\mathbf{u}$) are calculated by taking moments of the distribution functions:

* **Fluid Density ($\rho$):**

$$\rho = \sum_{i} f_i$$

* **Fluid Velocity ($\mathbf{u}$):**

$$\mathbf{u} = \frac{1}{\rho} \sum_{i} f_i \mathbf{e}_i$$

* **Fluid Pressure ($p$):**
Computed directly using the ideal gas equation of state for lattices, where $c_s = 1/\sqrt{3}$ is the lattice speed of sound:

$$p = \rho c_s^2 = \frac{\rho}{3}$$