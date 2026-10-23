import { ContactShadows, OrbitControls, RoundedBox, useGLTF } from '@react-three/drei'
import { Canvas } from '@react-three/fiber'
import { Suspense, useMemo } from 'react'

/**
 * Interactive 3D viewer, loaded on demand (this module and three.js are a separate chunk).
 *
 * When an administrator has attached a licensed glTF model to the vehicle it is shown with its attribution.
 * Otherwise a generic car shape built from primitives is shown and labelled as a demo, so it is never
 * mistaken for the actual vehicle.
 */
export default function Viewer3D({ modelUrl, bodyColour = '#e8a33d', label }: { modelUrl?: string | null; bodyColour?: string; label: string }) {
  return (
    <div className="relative h-[60vh] min-h-80 w-full overflow-hidden rounded-xl bg-gradient-to-b from-ink-100 to-sand-200">
      <Canvas camera={{ position: [4.2, 2.2, 5.2], fov: 38 }} dpr={[1, 2]} aria-label={label} role="img">
        <hemisphereLight args={['#ffffff', '#c9b99a', 1.1]} />
        <directionalLight position={[-6, 4, -4]} intensity={0.5} />
        <directionalLight position={[5, 8, 5]} intensity={1.4} />
        <Suspense fallback={null}>
          {modelUrl ? <GltfModel url={modelUrl} /> : <DemoCar colour={bodyColour} />}
        </Suspense>
        <ContactShadows position={[0, -0.01, 0]} opacity={0.45} scale={10} blur={2.4} far={4} />
        <OrbitControls enablePan={false} minDistance={4} maxDistance={10} maxPolarAngle={Math.PI / 2.05} autoRotate autoRotateSpeed={0.6} />
      </Canvas>
      <p className="pointer-events-none absolute bottom-3 left-3 rounded-full bg-white/85 px-3 py-1 text-xs font-semibold text-ink-700">
        Drag to rotate · scroll or pinch to zoom
      </p>
    </div>
  )
}

function GltfModel({ url }: { url: string }) {
  const { scene } = useGLTF(url)
  return <primitive object={scene} />
}

/** A stylised, generic car made of rounded boxes and cylinders. Not a likeness of any real model. */
function DemoCar({ colour }: { colour: string }) {
  const wheels = useMemo(() => [[-1.25, 0.36, 0.82], [1.25, 0.36, 0.82], [-1.25, 0.36, -0.82], [1.25, 0.36, -0.82]] as const, [])
  return (
    <group position={[0, 0, 0]}>
      <RoundedBox args={[3.9, 0.7, 1.75]} radius={0.22} smoothness={6} position={[0, 0.75, 0]}>
        <meshStandardMaterial color={colour} metalness={0.55} roughness={0.32} />
      </RoundedBox>
      <RoundedBox args={[2.1, 0.62, 1.55]} radius={0.24} smoothness={6} position={[-0.2, 1.32, 0]}>
        <meshStandardMaterial color="#1a2a42" metalness={0.3} roughness={0.08} transparent opacity={0.88} />
      </RoundedBox>
      {wheels.map(([x, y, z]) => (
        <group key={`${x}${z}`} position={[x, y, z]} rotation={[Math.PI / 2, 0, 0]}>
          <mesh><cylinderGeometry args={[0.36, 0.36, 0.26, 32]} /><meshStandardMaterial color="#111" roughness={0.8} /></mesh>
          <mesh position={[0, z > 0 ? 0.135 : -0.135, 0]}><cylinderGeometry args={[0.2, 0.2, 0.02, 24]} /><meshStandardMaterial color="#c9ccd1" metalness={0.9} roughness={0.25} /></mesh>
        </group>
      ))}
      {[0.55, -0.55].map((z) => (
        <mesh key={`h${z}`} position={[1.96, 0.82, z]}><boxGeometry args={[0.04, 0.12, 0.38]} /><meshStandardMaterial color="#fff6d6" emissive="#fff1b8" emissiveIntensity={0.6} /></mesh>
      ))}
      {[0.55, -0.55].map((z) => (
        <mesh key={`t${z}`} position={[-1.96, 0.85, z]}><boxGeometry args={[0.04, 0.1, 0.38]} /><meshStandardMaterial color="#b91c1c" emissive="#7f1d1d" emissiveIntensity={0.5} /></mesh>
      ))}
    </group>
  )
}
