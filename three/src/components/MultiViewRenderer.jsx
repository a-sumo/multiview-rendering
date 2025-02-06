import { useEffect, useRef } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls';
import { RGBELoader } from 'three/examples/jsm/loaders/RGBELoader';

const TEXTURE_SIZE = 256;
const DEPTH_SCALE = 100;
const BASE_PATH = '/textures/viewdepthmaps';

export default function MultiViewRenderer() {
  const containerRef = useRef();
  const sceneRef = useRef(new THREE.Scene());
  const cameraRef = useRef(null);
  const rendererRef = useRef(null);
  const controlsRef = useRef(null);
  const particlesRef = useRef(null);

  useEffect(() => {
    // Cleanup previous renderer
    while (containerRef.current?.firstChild) {
      containerRef.current.removeChild(containerRef.current.firstChild);
    }

    // Scene setup
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.5;
    renderer.outputEncoding = THREE.sRGBEncoding;
    containerRef.current.appendChild(renderer.domElement);
    rendererRef.current = renderer;

    // Camera setup
    const camera = new THREE.PerspectiveCamera(
      75,
      window.innerWidth / window.innerHeight,
      0.1,
      1000
    );
    camera.position.set(0, 0, 150);
    cameraRef.current = camera;

    // Environment map setup
    const pmremGenerator = new THREE.PMREMGenerator(renderer);
    pmremGenerator.compileEquirectangularShader();

    new RGBELoader()
      .setPath("/textures/")
      .load("studio_small_03_1k.hdr", (texture) => {
        const envMap = pmremGenerator.fromEquirectangular(texture).texture;
        sceneRef.current.environment = envMap;
        sceneRef.current.background = envMap;
        texture.dispose();
        pmremGenerator.dispose();
      }, undefined, (error) => console.error("HDR loading error:", error));

    // Lighting setup
    const ambientLight = new THREE.AmbientLight(0xffffff, 1.0);
    const directionalLight = new THREE.DirectionalLight(0xffffff, 1.0);
    directionalLight.position.set(1, 1, 1);
    sceneRef.current.add(ambientLight, directionalLight);

    // Controls setup
    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.05;
    controlsRef.current = controls;

    // Handle window resize
    const handleResize = () => {
      camera.aspect = window.innerWidth / window.innerHeight;
      camera.updateProjectionMatrix();
      renderer.setSize(window.innerWidth, window.innerHeight);
    };
    window.addEventListener("resize", handleResize);

    // Load depth texture and create particles
    const frame = "0001";
    const path = `${BASE_PATH}/${frame}nx.png`;

    new THREE.TextureLoader().load(path, (texture) => {
      texture.flipY = false;
      const canvas = document.createElement("canvas");
      const ctx = canvas.getContext("2d");
      canvas.width = TEXTURE_SIZE;
      canvas.height = TEXTURE_SIZE;

      const img = new Image();
      img.src = texture.image.src;
      img.onload = () => {
        ctx.drawImage(img, 0, 0, TEXTURE_SIZE, TEXTURE_SIZE);
        const imageData = ctx.getImageData(0, 0, TEXTURE_SIZE, TEXTURE_SIZE).data;

        // Create particle buffers
        const positions = new Float32Array(TEXTURE_SIZE * TEXTURE_SIZE * 3);
        const colors = new Float32Array(TEXTURE_SIZE * TEXTURE_SIZE * 3);
        let particleCount = 0;

        for (let y = 0; y < TEXTURE_SIZE; y++) {
          for (let x = 0; x < TEXTURE_SIZE; x++) {
            const index = (y * TEXTURE_SIZE + x) * 4;
            const r = imageData[index] / 255;
            const g = imageData[index + 1] / 255;
            const b = imageData[index + 2] / 255;
            const a = imageData[index + 3] / 255;

            if (a > 0.05) { // Alpha threshold
              const normalizedX = (x / TEXTURE_SIZE - 0.5) * DEPTH_SCALE;
              const normalizedY = (y / TEXTURE_SIZE - 0.5) * DEPTH_SCALE;
              const depth = (a - 0.5) * DEPTH_SCALE;

              positions[particleCount * 3] = normalizedX;
              positions[particleCount * 3 + 1] = normalizedY;
              positions[particleCount * 3 + 2] = depth;

              colors[particleCount * 3] = r;
              colors[particleCount * 3 + 1] = g;
              colors[particleCount * 3 + 2] = b;

              particleCount++;
            }
          }
        }

        // Create particle geometry
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute(
          'position',
          new THREE.BufferAttribute(positions.slice(0, particleCount * 3), 3)
        );
        geometry.setAttribute(
          'color',
          new THREE.BufferAttribute(colors.slice(0, particleCount * 3), 3)
        );

        // Create particle material
        const material = new THREE.PointsMaterial({
          size: 0.8,
          vertexColors: true,
          transparent: true,
          depthWrite: false,
          sizeAttenuation: true,
          metalness: 0.3,
          roughness: 0.7,
          envMapIntensity: 1.0
        });

        // Create particle system
        particlesRef.current = new THREE.Points(geometry, material);
        sceneRef.current.add(particlesRef.current);
      };
    }, undefined, (error) => console.error("Texture loading error:", error));

    // Animation loop
    const animate = () => {
      requestAnimationFrame(animate);
      controlsRef.current.update();
      renderer.render(sceneRef.current, cameraRef.current);
    };
    animate();

    // Cleanup
    return () => {
      renderer.dispose();
      if (particlesRef.current) {
        sceneRef.current.remove(particlesRef.current);
        particlesRef.current.geometry.dispose();
        particlesRef.current.material.dispose();
      }
      window.removeEventListener("resize", handleResize);
      containerRef.current?.removeChild(renderer.domElement);
    };
  }, []);

  return <div ref={containerRef} style={{ width: "100vw", height: "100vh" }} />;
}