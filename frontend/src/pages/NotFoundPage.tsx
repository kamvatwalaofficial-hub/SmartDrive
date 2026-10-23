import { Link } from 'react-router'

export default function NotFoundPage() {
  return (
    <div className="container-page py-20 text-center">
      <p className="text-6xl font-extrabold text-amber-500">404</p>
      <h1 className="mt-3 text-2xl">This road doesn&apos;t go anywhere</h1>
      <p className="mt-2 text-ink-500">The page you were looking for does not exist.</p>
      <Link to="/" className="btn-dark mt-6">Back to SmartDrive</Link>
    </div>
  )
}
