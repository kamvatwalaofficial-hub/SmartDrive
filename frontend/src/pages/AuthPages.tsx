import { zodResolver } from '@hookform/resolvers/zod'
import { useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { useAuth } from '../auth/AuthContext'
import { Field } from '../components/Field'
import { Alert } from '../components/ui'
import { applyServerErrors, passwordSchema, phoneSchema } from '../lib/forms'

/** Only same-site relative paths are accepted as a post-login destination. */
function safeNext(value: string | null): string {
  return value && value.startsWith('/') && !value.startsWith('//') ? value : '/'
}

const loginSchema = z.object({
  email: z.email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
})
type LoginValues = z.infer<typeof loginSchema>

export function LoginPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [error, setError] = useState<string | null>(null)
  const form = useForm<LoginValues>({ resolver: zodResolver(loginSchema), defaultValues: { email: '', password: '' } })
  const { register, handleSubmit, formState: { errors, isSubmitting } } = form

  const onSubmit = handleSubmit(async (values) => {
    setError(null)
    try {
      const user = await signIn(values.email, values.password)
      const next = safeNext(params.get('next'))
      navigate(next === '/' && user.roles.includes('Administrator') ? '/admin' : next, { replace: true })
    } catch (e) {
      setError(applyServerErrors(e, form.setError, ['email', 'password']))
    }
  })

  return (
    <AuthShell title="Sign in" subtitle={<>New to SmartDrive? <Link to={`/register${params.get('next') ? `?next=${encodeURIComponent(params.get('next')!)}` : ''}`} className="font-bold text-amber-700 underline">Create an account</Link></>}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {error && <Alert>{error}</Alert>}
        <Field label="Email" error={errors.email?.message} id="email">
          <input id="email" type="email" autoComplete="email" className={`input ${errors.email ? 'input-error' : ''}`} aria-invalid={!!errors.email} {...register('email')} />
        </Field>
        <Field label="Password" error={errors.password?.message} id="password">
          <input id="password" type="password" autoComplete="current-password" className={`input ${errors.password ? 'input-error' : ''}`} aria-invalid={!!errors.password} {...register('password')} />
        </Field>
        <button className="btn-primary w-full py-3" disabled={isSubmitting}>{isSubmitting ? 'Signing in…' : 'Sign in'}</button>
      </form>
      <div className="mt-6 rounded-xl bg-sand-100 p-3 text-xs text-ink-700">
        <p className="font-bold">Running the demo locally?</p>
        <p className="mt-1">The development seed creates a demo customer and a demo administrator. Their addresses and passwords are in <code>appsettings.Development.json</code> and the README.</p>
      </div>
    </AuthShell>
  )
}

const registerSchema = z.object({
  firstName: z.string().trim().min(1, 'Enter your first name.').max(60),
  lastName: z.string().trim().min(1, 'Enter your last name.').max(60),
  email: z.email('Enter a valid email address.').max(254),
  phoneNumber: phoneSchema.optional(),
  password: passwordSchema,
  confirm: z.string(),
}).refine((v) => v.password === v.confirm, { path: ['confirm'], message: 'The passwords do not match.' })
type RegisterValues = z.infer<typeof registerSchema>

export function RegisterPage() {
  const { register: createAccount } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [error, setError] = useState<string | null>(null)
  const form = useForm<RegisterValues>({ resolver: zodResolver(registerSchema), defaultValues: { firstName: '', lastName: '', email: '', phoneNumber: '', password: '', confirm: '' } })
  const { register, handleSubmit, formState: { errors, isSubmitting } } = form

  const onSubmit = handleSubmit(async ({ confirm: _confirm, ...values }) => {
    setError(null)
    try {
      await createAccount({ ...values, phoneNumber: values.phoneNumber || null })
      navigate(safeNext(params.get('next')), { replace: true })
    } catch (e) {
      setError(applyServerErrors(e, form.setError, ['firstName', 'lastName', 'email', 'phoneNumber', 'password']))
    }
  })

  return (
    <AuthShell title="Create your account" subtitle={<>Already registered? <Link to="/login" className="font-bold text-amber-700 underline">Sign in</Link></>}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {error && <Alert>{error}</Alert>}
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="First name" error={errors.firstName?.message} id="firstName">
            <input id="firstName" autoComplete="given-name" className="input" aria-invalid={!!errors.firstName} {...register('firstName')} />
          </Field>
          <Field label="Last name" error={errors.lastName?.message} id="lastName">
            <input id="lastName" autoComplete="family-name" className="input" aria-invalid={!!errors.lastName} {...register('lastName')} />
          </Field>
        </div>
        <Field label="Email" error={errors.email?.message} id="reg-email">
          <input id="reg-email" type="email" autoComplete="email" className="input" aria-invalid={!!errors.email} {...register('email')} />
        </Field>
        <Field label="Mobile number (optional)" error={errors.phoneNumber?.message} id="phone">
          <input id="phone" type="tel" autoComplete="tel" className="input" placeholder="e.g. 082 000 0000" {...register('phoneNumber')} />
        </Field>
        <Field label="Password" error={errors.password?.message} id="reg-password" hint="At least 10 characters with upper- and lowercase letters and a number.">
          <input id="reg-password" type="password" autoComplete="new-password" className="input" aria-invalid={!!errors.password} {...register('password')} />
        </Field>
        <Field label="Confirm password" error={errors.confirm?.message} id="confirm">
          <input id="confirm" type="password" autoComplete="new-password" className="input" aria-invalid={!!errors.confirm} {...register('confirm')} />
        </Field>
        <button className="btn-primary w-full py-3" disabled={isSubmitting}>{isSubmitting ? 'Creating account…' : 'Create account'}</button>
      </form>
    </AuthShell>
  )
}

function AuthShell({ title, subtitle, children }: { title: string; subtitle: ReactNode; children: ReactNode }) {
  return (
    <div className="container-page flex justify-center py-12">
      <div className="card w-full max-w-md p-6 sm:p-8">
        <h1 className="text-2xl">{title}</h1>
        <p className="mb-6 mt-1 text-sm text-ink-500">{subtitle}</p>
        {children}
      </div>
    </div>
  )
}
