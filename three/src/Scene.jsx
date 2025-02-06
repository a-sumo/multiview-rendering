import { useEffect, useRef } from 'react'
import * as THREE from 'three'
import NPYLoader from 'npyjs'

export default function Scene() {
  const containerRef = useRef()
  const animationRef = useRef()
  const sceneRef = useRef()
  const cameraRef = useRef()
  const rendererRef = useRef()
  const materialRef = useRef()

  const vertexShader = `
    precision highp float;
    attribute float vertexId;
    uniform sampler2D vatTexture;
    uniform float textureWidth;
    uniform float textureHeight;
    uniform float time;
    
    varying vec4 vColor;
    
    void main() {
      float u1 = (vertexId * 2.0 + 0.5) / textureWidth;
      float u2 = (vertexId * 2.0 + 1.5) / textureWidth;
      float v = (time + 0.5) / textureHeight;
      
      vec4 tex1 = texture2D(vatTexture, vec2(u1, v));
      vec4 tex2 = texture2D(vatTexture, vec2(u2, v));
      
      vec3 position = tex1.xyz;
      vColor = vec4(tex1.a, tex2.rg, tex2.b);
      
      gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
      gl_PointSize = 2.0;
    }
  `

  const fragmentShader = `
    precision highp float;
    varying vec4 vColor;
    
    void main() {
      gl_FragColor = vColor;
    }
  `

  useEffect(() => {
    // Scene setup
    const scene = new THREE.Scene()
    const camera = new THREE.PerspectiveCamera(75, 
      window.innerWidth / window.innerHeight, 0.1, 1000)
    const renderer = new THREE.WebGLRenderer({ antialias: true })
    
    sceneRef.current = scene
    cameraRef.current = camera
    rendererRef.current = renderer
    
    renderer.setSize(window.innerWidth, window.innerHeight)
    containerRef.current.appendChild(renderer.domElement)
    camera.position.z = 150

    // Load vertex animation texture
    const loader = new NPYLoader()
    loader.load('/vertex_animation_texture.npy', (data) => {
      const [textureHeight, textureWidth] = data.shape
      const texture = new THREE.DataTexture(
        data.data,
        textureWidth,
        textureHeight,
        THREE.RGBAFormat,
        THREE.FloatType
      )
      texture.needsUpdate = true

      // Create geometry with vertex IDs
      const numPoints = textureWidth / 2
      const geometry = new THREE.BufferGeometry()
      const vertexIds = new Float32Array(numPoints).map((_, i) => i)
      
      geometry.setAttribute('position', 
        new THREE.BufferAttribute(new Float32Array(numPoints * 3), 3))
      geometry.setAttribute('vertexId', 
        new THREE.BufferAttribute(vertexIds, 1))

      // Create material
      const material = new THREE.ShaderMaterial({
        uniforms: {
          vatTexture: { value: texture },
          textureWidth: { value: textureWidth },
          textureHeight: { value: textureHeight },
          time: { value: 0 }
        },
        vertexShader,
        fragmentShader,
        transparent: true
      })
      materialRef.current = material

      const points = new THREE.Points(geometry, material)
      scene.add(points)
    })

    // Animation loop
    const animate = (timestamp) => {
      if (materialRef.current) {
        materialRef.current.uniforms.time.value = timestamp / 1000
      }
      renderer.render(scene, camera)
      animationRef.current = requestAnimationFrame(animate)
    }
    animationRef.current = requestAnimationFrame(animate)

    // Cleanup
    return () => {
      cancelAnimationFrame(animationRef.current)
      renderer.dispose()
      if (containerRef.current) {
        containerRef.current.removeChild(renderer.domElement)
      }
    }
  }, [])

  return <div ref={containerRef} style={{ width: '100vw', height: '100vh' }} />
}